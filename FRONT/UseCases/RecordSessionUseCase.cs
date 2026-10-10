using DigitalBrain.UI.UseCases.Ports;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases;

/// <summary>
/// Caso de uso «grabar una sesión»: valida el plan, lanza la captura a través del backend,
/// analiza el archivo guardado con las mismas reglas que <c>build-dataset</c> y registra los metadatos.
/// </summary>
public sealed class RecordSessionUseCase(
    ICaptureGateway gateway,
    IRecordingRepository recordings,
    ISessionLog sessionLog)
{
    public async Task<RecordSessionResult> ExecuteAsync(
        RecordingPlan plan,
        Action<SignalSample> onSample,
        Action<string> onLog,
        CancellationToken cancellationToken)
    {
        var errors = plan.Validate();
        if (errors.Count > 0)
            return RecordSessionResult.Invalid(errors);

        Directory.CreateDirectory(plan.OutputDirectory);
        var request = new CaptureRequest(plan.ExecutablePath, plan.OutputPath, plan.DurationSeconds, plan.CaptureFromSeconds);
        var outcome = await gateway.CaptureAsync(request, onSample, onLog, cancellationToken);

        if (!outcome.Succeeded || outcome.SavedPath is null)
            return outcome.Cancelled ? RecordSessionResult.Canceled() : RecordSessionResult.Failure(outcome.Error ?? "La captura no terminó correctamente.");

        var summary = await recordings.AnalyzeAsync(outcome.SavedPath, CancellationToken.None)
                      ?? throw new InvalidOperationException($"No se pudo leer {outcome.SavedPath}.");
        summary = summary with { Movement = plan.Movement, DurationSeconds = plan.DurationSeconds };
        await sessionLog.AppendAsync(plan, summary, CancellationToken.None);
        return RecordSessionResult.Success(summary);
    }
}

public sealed record RecordSessionResult(bool Succeeded, bool Cancelled, RecordingSummary? Recording, IReadOnlyList<string> Errors)
{
    public static RecordSessionResult Success(RecordingSummary recording) => new(true, false, recording, []);
    public static RecordSessionResult Canceled() => new(false, true, null, []);
    public static RecordSessionResult Invalid(IReadOnlyList<string> errors) => new(false, false, null, errors);
    public static RecordSessionResult Failure(string error) => new(false, false, null, [error]);
}
