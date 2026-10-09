using GymShop.Application.Abstractions;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Coupons;
using GymShop.Application.DTOs.Coupons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cart")]
public class CartController : ApiControllerBase
{
    private readonly IGetCartUseCase _getCart;
    private readonly IAddCartItemUseCase _addCartItem;
    private readonly IUpdateCartItemUseCase _updateCartItem;
    private readonly IRemoveCartItemUseCase _removeCartItem;
    private readonly IClearCartUseCase _clearCart;
    private readonly ICheckoutCartUseCase _checkoutCart;
    private readonly IGetCheckoutSessionUseCase _getCheckout;
    private readonly ICurrentUserService _currentUser;
    private readonly IApplyCartCouponUseCase _applyCoupon;
    private readonly IRemoveCartCouponUseCase _removeCoupon;
    private readonly IShippingSettings _shippingSettings;
    private readonly IQuoteCartShippingUseCase _quoteShipping;

    public CartController(
        IGetCartUseCase getCart,
        IAddCartItemUseCase addCartItem,
        IUpdateCartItemUseCase updateCartItem,
        IRemoveCartItemUseCase removeCartItem,
        IClearCartUseCase clearCart,
        ICheckoutCartUseCase checkoutCart,
        IGetCheckoutSessionUseCase getCheckout,
        ICurrentUserService currentUser, IApplyCartCouponUseCase applyCoupon, IRemoveCartCouponUseCase removeCoupon,
        IShippingSettings shippingSettings,
        IQuoteCartShippingUseCase quoteShipping)
    {
        _getCart = getCart;
        _addCartItem = addCartItem;
        _updateCartItem = updateCartItem;
        _removeCartItem = removeCartItem;
        _clearCart = clearCart;
        _checkoutCart = checkoutCart;
        _getCheckout = getCheckout;
        _currentUser = currentUser;
        _applyCoupon = applyCoupon; _removeCoupon = removeCoupon;
        _shippingSettings = shippingSettings;
        _quoteShipping = quoteShipping;
    }

    [HttpGet("shipping-options")]
    [AllowAnonymous]
    public ActionResult<ShippingOptionsResponse> GetShippingOptions() => Ok(new ShippingOptionsResponse(
        _shippingSettings.HomeDeliveryCost,
        _shippingSettings.PickupAddress,
        _shippingSettings.PickupInstructions,
        _shippingSettings.PickupHours));

    [HttpPost("shipping-quotes")]
    public async Task<ActionResult<IReadOnlyList<ShippingQuoteResponse>>> QuoteShipping(CreateShippingQuoteRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _quoteShipping.ExecuteAsync(_currentUser.UserId, request, cancellationToken));
    }

    [HttpPost("coupon")]
    public async Task<ActionResult<CartResponse>> ApplyCoupon(ApplyCouponRequest request, CancellationToken cancellationToken)
    {
        var result = await _applyCoupon.ExecuteAsync(_currentUser.UserId, request, cancellationToken);
        return result.IsSuccess ? Ok(await _getCart.ExecuteAsync(_currentUser.UserId, cancellationToken)) : ToErrorResponse(result.Error!);
    }

    [HttpDelete("coupon")]
    public async Task<ActionResult<CartResponse>> RemoveCoupon(CancellationToken cancellationToken)
    {
        var result = await _removeCoupon.ExecuteAsync(_currentUser.UserId, cancellationToken);
        return result.IsSuccess ? Ok(await _getCart.ExecuteAsync(_currentUser.UserId, cancellationToken)) : ToErrorResponse(result.Error!);
    }

    [HttpGet]
    public async Task<ActionResult<CartResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _getCart.ExecuteAsync(_currentUser.UserId, cancellationToken));
    }

    [HttpPost("items")]
    public async Task<ActionResult<CartResponse>> AddItem(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _addCartItem.ExecuteAsync(_currentUser.UserId, request, cancellationToken));
    }

    [HttpPut("items/{productId:int}")]
    public async Task<ActionResult<CartResponse>> UpdateItem(int productId, [FromQuery] int? productVariantId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _updateCartItem.ExecuteAsync(_currentUser.UserId, productId, productVariantId, request, cancellationToken));
    }

    [HttpDelete("items/{productId:int}")]
    public async Task<ActionResult<CartResponse>> RemoveItem(int productId, [FromQuery] int? productVariantId, CancellationToken cancellationToken)
    {
        return FromResult(await _removeCartItem.ExecuteAsync(_currentUser.UserId, productId, productVariantId, cancellationToken));
    }

    [HttpDelete]
    public async Task<ActionResult> Clear(CancellationToken cancellationToken)
    {
        return FromResult(await _clearCart.ExecuteAsync(_currentUser.UserId, cancellationToken));
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout(CheckoutCartRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _checkoutCart.ExecuteAsync(_currentUser.UserId, request, cancellationToken));
    }

    [HttpGet("checkout/{id:int}")]
    public async Task<ActionResult<CheckoutResponse>> GetCheckout(int id, CancellationToken cancellationToken)
    {
        return FromResult(await _getCheckout.ExecuteAsync(id, _currentUser.UserId, cancellationToken));
    }
}
