using WhatsappBusiness.CloudApi.Messages.Requests;

namespace DRC.Api.Interfaces
{
    public interface IWhatAppService
    {
        Task<bool> SendMessage(string phone, string message);
        Task<bool> ReceiveMessage(string phone, string message);
        Task<bool> ReceiveLocation(string phone, double latitude, double longitude);
        Task<bool> SendTemplateMessage(string phone, string template, List<TextMessageComponent>? parameters = null);
    }
}
