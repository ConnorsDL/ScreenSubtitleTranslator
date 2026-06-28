namespace ScreenSubtitleTranslator.Translation;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
