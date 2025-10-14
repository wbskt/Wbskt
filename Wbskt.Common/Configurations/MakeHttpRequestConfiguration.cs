namespace Wbskt.Common.Configurations;

public class MakeHttpRequestConfiguration : StepConfigurationBase
{
    public required string Url { get; set; }
    public required string Method { get; set; }
    public string? Authentication { get; set; }
    public string? Headers { get; set; }
    public string? Body { get; set; }
}
