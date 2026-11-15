namespace OpenKSeF.Mobile.Models;

public class SetupStatusDto
{
    public bool IsInitialized { get; set; }
    public bool RegistrationAllowed { get; set; }
    public string? DataControllerName { get; set; }
    public string? DataControllerContact { get; set; }
}
