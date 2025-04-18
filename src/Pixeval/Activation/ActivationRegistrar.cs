// Copyright (c) Pixeval.
// Licensed under the GPL v3 License.

using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.AppLifecycle;
using Pixeval.CoreApi;
using Pixeval.Logging;
using Pixeval.Pages.Login;
using Pixeval.Util.Threading;
using Windows.ApplicationModel.Activation;

namespace Pixeval.Activation;

public static class ActivationRegistrar
{
    public static readonly List<IAppActivationHandler> FeatureHandlers =
    [
        new IllustrationAppActivationHandler(),
        new IllustratorAppActivationHandler(),
        new NovelAppActivationHandler()
    ];

    public static async void Dispatch(AppActivationArguments args)
    {
        if (args is
            {
                Kind: ExtendedActivationKind.Protocol, Data: IProtocolActivatedEventArgs { Uri: var activationUri }
            })
        {
            switch (activationUri.Scheme)
            {
                case "pixeval":
                {
                    if (FeatureHandlers.FirstOrDefault(f => f.ActivationFragment == activationUri.Host) is { } handler)
                    {
                        _ = handler.Execute(activationUri.PathAndQuery[1..]);
                    }

                    break;
                }
                case "pixiv":
                {
                    if (LoginPage.Current is null || LoginPage.CurrentVerifier is null || App.AppViewModel.MakoClient != null!)
                        return;
                    var code = HttpUtility.ParseQueryString(activationUri.Query)["code"]!;
                    var tokenResponse = await PixivAuth.AuthCodeToTokenResponseAsync(code, LoginPage.CurrentVerifier);
                    if (tokenResponse is null)
                        return;
                    var logger = App.AppViewModel.AppServiceProvider.GetRequiredService<FileLogger>();
                    App.AppViewModel.MakoClient = new MakoClient(tokenResponse, App.AppViewModel.AppSettings.ToMakoClientConfiguration(), logger);
                    _ = ThreadingHelper.DispatchAsync(LoginPage.SuccessNavigating);
                    break;
                }
            }
        }
    }
}
