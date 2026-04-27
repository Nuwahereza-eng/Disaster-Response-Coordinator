using Microsoft.AspNetCore.Components;

namespace DRC.App.Components
{
    public partial class App : ComponentBase
    {
        /// <summary>
        /// API base URL exposed to the PWA service worker / pwa.js via window.DRC_API.
        /// Set once at startup from the same source as the typed HttpClient base address.
        /// </summary>
        public static string ApiBaseUrl { get; set; } = "";
    }
}
