using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GymShop.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.RateLimiting;

//Objeto creado que le pasamos por parametro un booleano y un timespan, solo se leera en este archivo y es solo lectura.
//ese retryAfter luego se convertirá en header para httpContext, para que le diga al usuario cuanto tiene que esperar.

public readonly record struct RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter);

public interface IGymShopRequestLimiter
{
    RateLimitDecision Acquire(string policy, string partitionKey);
}

public sealed class GymShopRequestLimiter : IGymShopRequestLimiter
{
    //Counter: es un objeto que guarda el id de la ventana temporal y un contador de la cantidad de solicitudes desde esa ventana.
    //ConcurrentDictionary: es un diccionario que guarda la key y el counter. la key es la politica y la particion de la key, el counter guarda el id de la ventana y el contador.
    //ademas se inicia la instancia ahi, es decir, cada vez que se levanta la app inicia la instancia
    //options: contiene las politicas cargadas en appsetting.json
    //TimeProvider: es abstraccion de .net para saber el tiempo pero desde el sistema, permite controles de los test
    //operations: contador del rate limiter, cuando llega a 1024 se limpia la memoria
    private sealed record Counter(long Window, int Count);
    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly GymShopRateLimitingOptions _options;
    private readonly TimeProvider _timeProvider;
    private long _operations;


    //inyectamos a traves de una interfaz a options y a timeProvider
    //no inyectamos operations porque no viene de afuera, solo se controla aca
    public GymShopRequestLimiter(IOptions<GymShopRateLimitingOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /*
       * la funcion acquire registra un intento por particion y politica, ademas verifica si pasa o no.
       * Acquire devolvera objeto ratelimitdecision (bool y timespan)
       * el if verifica si la politica esta habilitada, si no, devuelve true y timespan cero.
       * options esta configurada como singleton en program.cs
       * si existe regla se guarda en la variable rule, guarda los segundos en unix y guarda la politica y particion en variable key
       * window es la division de el tiempo en unix y los tiempos de las politicas, dando ventanas segundos dependiendo la politica. se pueden hacer una cantidad de intentos dentro de esa ventana
       * if de interlocked incremenda las solicitudes hasta 1024, luego limpia la memoria dentro del bloque.
       * el bloque filtra por politicas que sean iguales a la actual y que sean menores al window - 1
       * la variable counter genera una nueva clave o actualiza.
       * verifica si hay una window current, y guarda esa y le agrega 1 al contador, sino crea una nueva ventana con contador 1.
       * el if final verifica si el contador es menor al numero de intentos, si es asi, deja pasar, sino retorna false.
    */
    public RateLimitDecision Acquire(string policy, string partitionKey)
    {
        if (!_options.Enabled) return new RateLimitDecision(true, TimeSpan.Zero);
        var rule = GetRule(policy);
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var window = now / rule.WindowSeconds;
        var key = $"{policy}:{partitionKey}";
        if (Interlocked.Increment(ref _operations) % 1024 == 0)
        {
            foreach (var stale in _counters.Where(x => x.Key.StartsWith(policy + ":", StringComparison.Ordinal) && x.Value.Window < window - 1))
            {
                _counters.TryRemove(stale.Key, out _);
            }
        }

        var counter = _counters.AddOrUpdate(key, _ => new Counter(window, 1), (_, current) =>
            current.Window == window ? current with { Count = current.Count + 1 } : new Counter(window, 1));

        if (counter.Count <= rule.PermitLimit) return new RateLimitDecision(true, TimeSpan.Zero);
        var retrySeconds = ((window + 1) * rule.WindowSeconds) - now;
        return new RateLimitDecision(false, TimeSpan.FromSeconds(Math.Max(1, retrySeconds)));
    }


    /*
        * getRule devuelve un objeto RateLimitRule
     */
    private RateLimitRule GetRule(string policy) => policy switch
    {
        RateLimitPolicies.LoginAccount => _options.LoginAccount,
        RateLimitPolicies.RegistrationGlobal => _options.RegistrationGlobal,
        RateLimitPolicies.PasswordResetAccount => _options.PasswordResetAccount,
        RateLimitPolicies.PaymentUser => _options.PaymentUser,
        RateLimitPolicies.PaymentOrder => _options.PaymentOrder,
        RateLimitPolicies.WebhookGlobal => _options.WebhookGlobal,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown rate-limit policy.")
    };

    public static string HashAccount(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}

public static class RateLimitPolicies
{
    public const string LoginIp = "login-ip";
    public const string LoginAccount = "login-account";
    public const string RegistrationIp = "registration-ip";
    public const string RegistrationGlobal = "registration-global";
    public const string PasswordResetAccount = "password-reset-account";
    public const string PasswordResetIp = "password-reset-ip";
    public const string PaymentUser = "payment-user";
    public const string PaymentOrder = "payment-order";
    public const string WebhookIp = "webhook-ip";
    public const string WebhookGlobal = "webhook-global";
}

/*
    * Este objeto tiene como finalidad, poner en palabras al rechazo de la request.
    * Create recibe httpcontext y decision, que es Acquire (bool, timespan) y es un objeto RateLimitDecision
    * devuelve objectResult que contiene status, title y detail.
    * calculamos los segundos que deberia esperar, luego se lo pasamos al header de retryAfter
    * invariantCulture lo convierte a un formato tecnico, y acorde al pais.
    * ademas se usa la libreria extends para la telemetria y observabilidad.
 */
public static class RateLimitResponse
{
    public static ObjectResult Create(HttpContext context, RateLimitDecision decision)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Demasiadas solicitudes.",
            Detail = "Se alcanzo el limite temporal de solicitudes. Intenta nuevamente mas tarde."
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
            ContentTypes = { "application/problem+json" }
        };
    }
}
