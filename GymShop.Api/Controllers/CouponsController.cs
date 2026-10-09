using GymShop.Application.DTOs.Coupons;
using GymShop.Application.UseCases.Coupons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController, Authorize(Roles = "Admin,SuperAdmin"), Route("api/coupons")]
public class CouponsController(IGetCouponsUseCase list, IGetCouponUseCase get, ICreateCouponUseCase create, IUpdateCouponUseCase update, IUpdateCouponStatusUseCase status) : ApiControllerBase
{
    [HttpGet] public async Task<ActionResult<PagedCouponsResponse>> List([FromQuery] CouponFilterRequest request, CancellationToken ct) => FromResult(await list.ExecuteAsync(request, ct));
    [HttpGet("{id:int}")] public async Task<ActionResult<CouponResponse>> Get(int id, CancellationToken ct) => FromResult(await get.ExecuteAsync(id, ct));
    [HttpPost] public async Task<ActionResult<CouponResponse>> Create(UpsertCouponRequest request, CancellationToken ct) => FromResult(await create.ExecuteAsync(request, ct));
    [HttpPut("{id:int}")] public async Task<ActionResult<CouponResponse>> Update(int id, UpsertCouponRequest request, CancellationToken ct) => FromResult(await update.ExecuteAsync(id, request, ct));
    [HttpPatch("{id:int}/status")] public async Task<ActionResult> Status(int id, UpdateCouponStatusRequest request, CancellationToken ct) => FromResult(await status.ExecuteAsync(id, request, ct));
}
