# GymShop

E-commerce de equipamiento para gimnasio con catálogo, carrito, checkout invitado, gestión de stock, panel administrativo y pagos por transferencia bancaria o Mercado Pago.

El repositorio contiene una API ASP.NET Core, una aplicación React y una base PostgreSQL administrada con Entity Framework Core.

## Funcionalidades

- catálogo público con categorías, variantes, atributos e imágenes;
- carrito para clientes registrados e invitados;
- compra sin crear una cuenta;
- transferencia bancaria con orden pendiente, vencimiento configurable y referencia numérica única;
- Mercado Pago con creación de la orden únicamente después de confirmar el pago;
- validación de stock al acreditar una transferencia, sin reservar productos mientras está pendiente;
- notificaciones transaccionales por email;
- administración de productos, categorías, stock, pedidos, cupones y usuarios;
- autenticación con cookies seguras, renovación de sesión, Google y MFA administrativo;
- auditoría, rate limiting, webhooks idempotentes y protección de concurrencia;
- recibos internos y base preparada para facturación electrónica;
- SEO configurable, sitemap y datos estructurados;
- footer preparado para Instagram, Facebook y correo.

## Stack

| Área | Tecnología |
| --- | --- |
| Backend | .NET 10, ASP.NET Core, Entity Framework Core |
| Frontend | React 19, TypeScript, Vite |
| Base de datos | PostgreSQL / Neon |
| Pagos | Transferencia bancaria, Mercado Pago |
| Archivos | Object Storage compatible con S3 |
| Tests | xUnit, Vitest, Testing Library |
| Infraestructura local | Docker Compose |

## Estructura

```text
GymShop.Api/             API HTTP, seguridad y configuración
GymShop.Application/     casos de uso, DTOs y reglas de aplicación
GymShop.Domain/          entidades y enumeraciones del dominio
GymShop.Infrastructure/  PostgreSQL, pagos, email, archivos e integraciones
GymShop.Tests/           pruebas unitarias e integración
GymShop.Web/             aplicación React
docker/                  inicialización de PostgreSQL local
scripts/                 herramientas operativas
```

## Flujo de compra

### Transferencia bancaria

1. El cliente confirma sus datos y elige transferencia.
2. Se crea una orden `Pending` con una referencia numérica de nueve dígitos.
3. Se envían por email la referencia, los datos bancarios, el vencimiento y el enlace privado de consulta.
4. El stock no queda reservado y el cliente puede seguir comprando.
5. Un administrador verifica la acreditación.
6. Al confirmar el pago se valida nuevamente el stock y se descuenta en una transacción.
7. Si ya no hay disponibilidad, la orden se cancela y queda marcada para gestionar un cambio o una devolución.

El vencimiento predeterminado es de 24 horas y se configura con `BankTransfer__PendingOrderLifetimeHours`.

### Mercado Pago

1. Se crea una sesión de checkout, no una orden pendiente.
2. El cliente completa el pago en Mercado Pago.
3. El webhook verificado consulta el pago al proveedor.
4. La orden se crea solamente cuando el pago queda aprobado.

## Requisitos

- .NET SDK 10;
- Node.js 22 o superior;
- pnpm;
- PostgreSQL 17 o Docker Desktop.

## Configuración local

### Backend

La API lee configuración de `appsettings.json`, variables de entorno y User Secrets. No guardes credenciales reales en el repositorio.

Variables mínimas:

```env
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=GymShopDb;Username=gymshop_app;Password=<PASSWORD>
Jwt__Secret=<SECRETO_DE_AL_MENOS_32_CARACTERES>
Mfa__EncryptionKey=<CLAVE_BASE64_DE_32_BYTES>
```

Para aplicar migraciones con un rol separado:

```env
DATABASE_MIGRATION_URL=Host=localhost;Port=5432;Database=GymShopDb;Username=gymshop_migrator;Password=<PASSWORD>
DatabaseInitialization__Enabled=true
```

El inicializador aplica migraciones y puede crear el SuperAdmin configurado. No inserta productos ni categorías de demostración.

Ejecutar la API:

```powershell
dotnet run --project GymShop.Api
```

### Frontend

Copiá `GymShop.Web/.env.example` a `GymShop.Web/.env.local` y ajustá los valores públicos.

```powershell
cd GymShop.Web
pnpm install
pnpm dev
```

Por defecto, Vite abre `http://localhost:5173` y utiliza el proxy definido en `vite.config.ts` para acceder a la API.

## Transferencia bancaria

Los datos se cargan como secretos del entorno del backend:

```env
BankTransfer__BankName=<BANCO>
BankTransfer__AccountHolder=<TITULAR>
BankTransfer__Cbu=<CBU>
BankTransfer__Alias=<ALIAS>
BankTransfer__Cuit=<CUIT_OPCIONAL>
BankTransfer__PendingOrderLifetimeHours=24
```

En Docker Compose se usan las variables equivalentes:

```env
GYMSHOP_BANK_NAME=
GYMSHOP_BANK_ACCOUNT_HOLDER=
GYMSHOP_BANK_CBU=
GYMSHOP_BANK_ALIAS=
GYMSHOP_BANK_CUIT=
GYMSHOP_BANK_PENDING_ORDER_LIFETIME_HOURS=24
```

La referencia que debe informar el cliente se genera para cada pago y se guarda en `Payments.ExternalReference`.

## Mercado Pago

```env
MercadoPago__Enabled=true
MercadoPago__AccessToken=<ACCESS_TOKEN>
MercadoPago__PublicKey=<PUBLIC_KEY>
MercadoPago__WebhookSecret=<WEBHOOK_SECRET>
MercadoPago__NotificationUrl=https://api.ejemplo.com/api/payments/webhook
MercadoPago__CheckoutSuccessUrl=https://tienda.ejemplo.com/checkout/pago/{checkoutId}
MercadoPago__CheckoutFailureUrl=https://tienda.ejemplo.com/checkout/pago/{checkoutId}
MercadoPago__CheckoutPendingUrl=https://tienda.ejemplo.com/checkout/pago/{checkoutId}
```

El webhook debe ser HTTPS público y conservar la validación de firma. No habilites Mercado Pago con credenciales de producción hasta validar todo el flujo en una cuenta de prueba.

## Email

```env
Email__Provider=Resend
Email__ApiKey=<API_KEY>
Email__FromAddress=ventas@ejemplo.com
Email__FromName=GymShop
Email__PublicAppUrl=https://tienda.ejemplo.com
Email__TransactionalNotificationsEnabled=true
```

En desarrollo puede utilizarse el proveedor `Mock`.

## Redes sociales y SEO

Variables públicas del frontend:

```env
VITE_SITE_URL=https://tienda.ejemplo.com
VITE_SITE_NAME=GymShop
VITE_DEFAULT_SEO_DESCRIPTION=<DESCRIPCION>
VITE_SEO_IMAGE=/imagen-social-1200x630.png
VITE_INSTAGRAM_URL=
VITE_FACEBOOK_URL=
VITE_SOCIAL_EMAIL=
```

Los iconos sociales se muestran deshabilitados mientras sus valores estén vacíos. El build genera `robots.txt` y `sitemap.xml`; con un dominio temporal se mantiene `noindex` para evitar indexación accidental.

## Docker

Creá `.env.docker` a partir de `.env.docker.example` y reemplazá todos los secretos.

```powershell
docker compose --env-file .env.docker up --build -d
docker compose ps
docker compose logs -f api
```

Para detener los servicios conservando la base:

```powershell
docker compose down
```

El volumen `gymshop-postgres-data` contiene los datos locales. Eliminarlo borra la base y requiere una decisión explícita.

## Base de datos y migraciones

Las migraciones PostgreSQL están en `GymShop.Infrastructure/Data/PostgresMigrations`.

```powershell
dotnet ef database update --project GymShop.Infrastructure --startup-project GymShop.Api
```

En Neon se recomienda:

- conexión pooled para tráfico normal de la aplicación;
- conexión directa para migraciones y tareas administrativas;
- rama `Development` para pruebas y rama `production` para datos reales;
- revisar el destino antes de ejecutar cualquier limpieza.

La limpieza de datos de prueba debe conservar `__EFMigrationsHistory`, los roles y al menos un SuperAdmin operativo. No ejecutes `TRUNCATE ... CASCADE` sin revisar el alcance.

## Tests

Backend completo:

```powershell
dotnet test GymShop.slnx
```

Backend sin las pruebas que requieren PostgreSQL:

```powershell
dotnet test GymShop.slnx --filter "FullyQualifiedName!~GymShop.Tests.Integration"
```

Frontend:

```powershell
cd GymShop.Web
pnpm test
pnpm lint
pnpm typecheck
pnpm build
```

Las pruebas de integración aceptan `GYMSHOP_TEST_POSTGRES` y crean bases aisladas que eliminan al finalizar. El rol utilizado debe poder crear y borrar bases de datos.

## Seguridad operativa

- no subir `.env`, tokens, contraseñas, CBU ni claves privadas;
- usar roles separados para migraciones y ejecución de la aplicación;
- mantener HTTPS, cookies `HttpOnly` y orígenes CORS explícitos;
- rotar secretos si aparecen en logs o commits;
- verificar manualmente la acreditación antes de aprobar una transferencia;
- probar migraciones y limpiezas en `Development` antes de producción.

## CI

El workflow de GitHub Actions levanta PostgreSQL, ejecuta la suite .NET y valida el frontend. Las migraciones se aplican desde cero en las pruebas de integración para detectar diferencias entre el modelo y el esquema real.
