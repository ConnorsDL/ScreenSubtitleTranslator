namespace ScreenSubtitleTranslator.ViewModels;

public sealed class PipelineModuleStatus : ViewModelBase
{
    private string _state;
    private string _details;

    public PipelineModuleStatus(string name, string state, string details)
    {
        Name = name;
        _state = state;
        _details = details;
    }

    public string Name { get; }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }
}
