using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;

namespace api.Tests;

/// <summary>
/// Contract tests for the Web Push API functions:
///   POST /api/subscribe  — stores a PushSubscription
///   POST /api/notify     — fans out a push notification
///   GET  /api/push/vapid-public-key — returns the VAPID public key
/// </summary>
public class PushFunctionTests
{
    private static readonly Assembly ApiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;

    [Fact]
    public void SubscribeFunction_MustExist()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.SubscribeFunction");
        type.Should().NotBeNull(
            "SubscribeFunction must exist — POST /api/subscribe stores the browser PushSubscription");
    }

    [Fact]
    public void SubscribeFunction_MustHave_PostHttpTrigger()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.SubscribeFunction");
        type.Should().NotBeNull();

        var method = type!.GetMethods()
            .FirstOrDefault(m => m.GetParameters().Any(p =>
                p.GetCustomAttributes(false)
                    .Any(a => a is HttpTriggerAttribute ht && ht.Methods != null && ht.Methods.Contains("post"))));

        method.Should().NotBeNull(
            "SubscribeFunction must have a POST HttpTrigger parameter");
    }

    [Fact]
    public void NotifyFunction_MustExist()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.NotifyFunction");
        type.Should().NotBeNull(
            "NotifyFunction must exist — POST /api/notify fans out push notifications");
    }

    [Fact]
    public void NotifyFunction_MustHave_PostHttpTrigger()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.NotifyFunction");
        type.Should().NotBeNull();

        var method = type!.GetMethods()
            .FirstOrDefault(m => m.GetParameters().Any(p =>
                p.GetCustomAttributes(false)
                    .Any(a => a is HttpTriggerAttribute ht && ht.Methods != null && ht.Methods.Contains("post"))));

        method.Should().NotBeNull(
            "NotifyFunction must have a POST HttpTrigger parameter");
    }

    [Fact]
    public void NotifyFunction_MustExpose_VapidPublicKey_Endpoint()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.NotifyFunction");
        type.Should().NotBeNull();

        // Should have a GET handler for the VAPID public key
        var method = type!.GetMethods()
            .FirstOrDefault(m => m.GetParameters().Any(p =>
                p.GetCustomAttributes(false)
                    .Any(a => a is HttpTriggerAttribute ht && ht.Methods != null && ht.Methods.Contains("get"))));

        method.Should().NotBeNull(
            "NotifyFunction must have a GET endpoint to expose the VAPID public key to the client");
    }

    [Fact]
    public void PushSubscriptionStore_MustExist()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.Services.PushSubscriptionStore");
        type.Should().NotBeNull(
            "PushSubscriptionStore must exist — stores subscriptions in blob storage");
    }

    [Fact]
    public void PushSubscriptionStore_MustHave_SaveAndGetAllMethods()
    {
        var type = ApiAssembly.GetType("DaeanneFrontend.Api.Services.PushSubscriptionStore");
        type.Should().NotBeNull();

        type!.GetMethod("SaveAsync").Should().NotBeNull(
            "PushSubscriptionStore.SaveAsync must exist — persist a new subscription");
        type.GetMethod("GetAllAsync").Should().NotBeNull(
            "PushSubscriptionStore.GetAllAsync must exist — retrieve all subscriptions for fan-out");
        type.GetMethod("DeleteAsync").Should().NotBeNull(
            "PushSubscriptionStore.DeleteAsync must exist — remove stale subscriptions");
    }
}
