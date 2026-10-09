using GymShop.Application.DTOs.Attributes;
using GymShop.Application.UseCases.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController, Route("api/attributes")]
public class AttributesController(IAttributeAdminService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AttributeResponse>>> Get(CancellationToken ct) => Ok(await service.GetAsync(false, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpGet("admin")]
    public async Task<ActionResult<List<AttributeResponse>>> Admin(CancellationToken ct) => Ok(await service.GetAsync(true, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPost]
    public async Task<ActionResult<AttributeResponse>> Create(UpsertAttributeRequest request, CancellationToken ct) { var r = await service.CreateAsync(request, ct); return r.IsSuccess ? Created($"/api/attributes/{r.Value!.Id}", r.Value) : ToErrorResponse(r.Error!); }
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPut("{id:int}")]
    public async Task<ActionResult<AttributeResponse>> Update(int id, UpsertAttributeRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPatch("{id:int}/status")]
    public async Task<ActionResult> Status(int id, UpdateAttributeStatusRequest request, CancellationToken ct) => FromResult(await service.SetStatusAsync(id, request.IsActive, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPost("{id:int}/options")]
    public async Task<ActionResult<AttributeOptionResponse>> AddOption(int id, UpsertAttributeOptionRequest request, CancellationToken ct) => FromResult(await service.AddOptionAsync(id, request, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPut("{id:int}/options/{optionId:int}")]
    public async Task<ActionResult<AttributeOptionResponse>> UpdateOption(int id, int optionId, UpsertAttributeOptionRequest request, CancellationToken ct) => FromResult(await service.UpdateOptionAsync(id, optionId, request, ct));
    [Authorize(Roles = "Admin,SuperAdmin"), HttpPatch("{id:int}/options/{optionId:int}/status")]
    public async Task<ActionResult> OptionStatus(int id, int optionId, UpdateAttributeStatusRequest request, CancellationToken ct) => FromResult(await service.SetOptionStatusAsync(id, optionId, request.IsActive, ct));
}
