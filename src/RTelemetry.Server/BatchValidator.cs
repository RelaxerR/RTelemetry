using RTelemetry.Contracts;

namespace RTelemetry.Server;

/// <summary>Проверка пачки по ограничениям схемы. Возвращает текст ошибки или null.</summary>
public static class BatchValidator
{
    public static string? Validate(TelemetryBatch? batch)
    {
        if (batch is null) return "Empty body.";
        if (batch.SchemaVersion != TelemetrySchema.CurrentVersion) return $"Unsupported schemaVersion {batch.SchemaVersion}.";
        if (!TelemetrySchema.IsValidName(batch.Project)) return "Invalid project.";
        if (batch.InstallId == Guid.Empty) return "Missing installId.";
        if (batch.Platform is null || batch.Platform.Length > TelemetrySchema.MaxStringValueLength) return "Platform is too long.";
        if (batch.Events is null || batch.Events.Count == 0) return "No events.";
        if (batch.Events.Count > TelemetrySchema.MaxEventsPerBatch) return $"More than {TelemetrySchema.MaxEventsPerBatch} events.";

        for (var i = 0; i < batch.Events.Count; i++)
        {
            var e = batch.Events[i];
            if (e is null) return $"events[{i}]: null.";
            if (e.Id == Guid.Empty) return $"events[{i}]: missing id.";
            if (!TelemetrySchema.IsValidName(e.Name)) return $"events[{i}]: invalid name.";
            if (e.SessionId == Guid.Empty) return $"events[{i}]: missing sessionId.";
            if (string.IsNullOrEmpty(e.AppVersion) || e.AppVersion.Length > TelemetrySchema.MaxNameLength) return $"events[{i}]: invalid appVersion.";
            if (e.ContentVersion is { Length: > TelemetrySchema.MaxNameLength }) return $"events[{i}]: contentVersion is too long.";

            if (e.Props is null) continue;
            if (e.Props.Count > TelemetrySchema.MaxPropsPerEvent) return $"events[{i}]: too many props.";
            foreach (var (key, value) in e.Props)
            {
                if (!TelemetrySchema.IsValidName(key)) return $"events[{i}]: invalid prop name.";
                if (!TelemetrySchema.IsValidPropValue(value)) return $"events[{i}].{key}: invalid value.";
            }
        }

        return null;
    }
}
