using eShop.Basket.API.Grpc;
using GrpcBasketItem = eShop.Basket.API.Grpc.BasketItem;
using GrpcBasketClient = eShop.Basket.API.Grpc.Basket.BasketClient;
using System.Diagnostics;

namespace eShop.WebApp.Services;

public class BasketService(GrpcBasketClient basketClient)
{

    private static readonly ActivitySource activitySource = new("WebApp");

    public async Task<IReadOnlyCollection<BasketQuantity>> GetBasketAsync()
    {   
        using var activity = activitySource.StartActivity("GetBasketAsync", ActivityKind.Client);
        activity?.AddEvent(new ActivityEvent("[WEB_APP]: Request sent to GetBasket in Basket.API"));
        var result = await basketClient.GetBasketAsync(new ());
        return MapToBasket(result);
    }

    public async Task DeleteBasketAsync()
    {
        await basketClient.DeleteBasketAsync(new DeleteBasketRequest());
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        using var activity = activitySource.StartActivity("UpdateBasketAsync", ActivityKind.Client);

        var updatePayload = new UpdateBasketRequest();
        foreach (var item in basket)
        {
            var updateItem = new GrpcBasketItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            };
            updatePayload.Items.Add(updateItem);
        }

        activity?.AddEvent(new ActivityEvent("[WEB_APP]: Request sent to update basket"));

        await basketClient.UpdateBasketAsync(updatePayload);
    }

    private static List<BasketQuantity> MapToBasket(CustomerBasketResponse response)
    {
        var result = new List<BasketQuantity>();
        foreach (var item in response.Items)
        {
            result.Add(new BasketQuantity(item.ProductId, item.Quantity));
        }

        return result;
    }
}

public record BasketQuantity(int ProductId, int Quantity);
