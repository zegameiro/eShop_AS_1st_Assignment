using System.Diagnostics;
using OpenTelemetry;

public class MaskingActivityProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.Tags.Any(tag => tag.Key == "user.id"))
        {
            var userId = activity.Tags.FirstOrDefault(tag => tag.Key == "user.id").Value;
            var maskedUserId = MaskUserId(userId);
            activity.SetTag("user.id", maskedUserId);
        }
    }

    private static string MaskUserId(string userId)
    {
        return userId.Substring(0, 2) + new string('*', userId.Length - 2);
    }
}