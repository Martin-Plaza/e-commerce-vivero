using GymShop.Application.Abstractions;

namespace GymShop.Tests.TestSupport;

internal sealed record FakeAuditContext(int? ActorUserId, string CorrelationId) : IAuditContext;
