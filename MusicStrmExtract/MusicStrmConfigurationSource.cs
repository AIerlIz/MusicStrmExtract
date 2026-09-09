namespace MusicStrmExtract;

internal interface IMusicStrmConfigurationSource
{
    PluginConfiguration Current { get; }
}

internal sealed class MusicStrmConfigurationSource : IMusicStrmConfigurationSource
{
    public static readonly MusicStrmConfigurationSource Default = new();

    public PluginConfiguration Current => Plugin.GetConfiguration();
}
