namespace RTelemetry.Client;

/// <summary>Согласие на сбор телеметрии.</summary>
public enum ConsentState
{
    /// <summary>Игрока ещё не спрашивали. События не пишутся и не отправляются.</summary>
    Unknown = 0,

    /// <summary>Согласие дано: события пишутся и отправляются.</summary>
    Granted = 1,

    /// <summary>Отказ или отзыв согласия: очередь стирается, новые события не пишутся.</summary>
    Denied = 2,
}
