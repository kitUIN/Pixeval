// Copyright (c) Pixeval.
// Licensed under the GPL v3 License.

using System;
using Pixeval.Util.Threading;
using System.Threading.Tasks;
using Pixeval.Pages.NovelViewer;
using Pixeval.Util.UI;
using Pixeval.Utilities;

namespace Pixeval.Activation;

public class NovelAppActivationHandler : IAppActivationHandler
{
    public string ActivationFragment => "novel";

    public Task Execute(string param)
    {
        if (!long.TryParse(param, out var id))
        {
            return Task.CompletedTask;
        }

        return ThreadingHelper.DispatchTaskAsync(async () =>
        {
            try
            {
                await NovelViewerHelper.CreateWindowWithPageAsync(id);
            }
            catch (Exception e)
            {
                AppNotificationHelper.ShowTextAppNotification(
                    ActivationsResources.ActivationFailedTitle,
                    ActivationsResources.ActivationFailedContentFormatted.Format(e.Message));
            }
        });
    }
}

