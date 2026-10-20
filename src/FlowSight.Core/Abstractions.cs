using FlowSight.Core.Model;

namespace FlowSight.Core;

/// <summary>Supplies the event log: synthetic data, a CSV file or a live system.</summary>
public interface IEventSource
{
    EventLog Load();
}
