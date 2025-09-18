using System.Reflection;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class BrioIpc : IpcSubscriber
{
    private FieldInfo? _brioActorEntityRawNameField;
    private Type? _brioActorEntityType;
    private ConstructorInfo? _brioEntityIdConstructor;
    private Type? _brioEntityIdType;
    private object? _brioEntityManagerInstance;
    private Type? _brioEntityManagerType;
    private Type? _brioEntityType;
    private MethodInfo? _brioGetServiceMethod;
    private bool _brioSearched;
    private FieldInfo? _brioServicesField;
    private MethodInfo? _brioTryGetEntityMethod;

    public BrioIpc() : base("Brio")
    {
    }

    public string? GetActorName(IGameObject actor)
    {
        if (!IsReady()) return null;

        InitializeBrioIpc();

        if (_brioEntityManagerInstance == null) return null;

        try
        {
            var entityIdString = $"actor_{actor.Address}";
            var entityId = _brioEntityIdConstructor!.Invoke(new object[] { entityIdString });

            var parameters = new[] { entityId, null };
            var result = (bool)_brioTryGetEntityMethod!.Invoke(_brioEntityManagerInstance, parameters)!;

            if (!result) return null;

            var brioEntity = parameters[1];
            if (brioEntity == null || brioEntity.GetType() != _brioActorEntityType) return null;

            var rawName = _brioActorEntityRawNameField!.GetValue(brioEntity) as string;
            return string.IsNullOrEmpty(rawName) ? null : rawName;
        }
        catch (Exception ex)
        {
            PluginLog.Verbose($"Error during Brio GetActorName reflection: {ex.Message}");
            return null;
        }
    }

    private void InitializeBrioIpc()
    {
        if (_brioSearched) return;

        PluginLog.Debug("[Brio IPC] Starting initialization...");

        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin("Brio", out var brioPlugin, false, true))
                return;

            var brioAssembly = brioPlugin.GetType().Assembly;

            _brioServicesField = brioAssembly.GetType("Brio.Brio")
                ?.GetField("_services", BindingFlags.NonPublic | BindingFlags.Static);

            var serviceProvider = _brioServicesField?.GetValue(null);
            if (serviceProvider == null) return;

            _brioGetServiceMethod = serviceProvider.GetType().GetMethod("GetService", new[] { typeof(Type) });
            _brioEntityManagerType = brioAssembly.GetType("Brio.Entities.EntityManager");

            if (_brioGetServiceMethod == null || _brioEntityManagerType == null)
            {
                PluginLog.Error("[Brio IPC] Could not find critical reflection members. Brio may have updated.");
                _brioSearched = true;
                return;
            }

            _brioEntityManagerInstance =
                _brioGetServiceMethod.Invoke(serviceProvider, new object[] { _brioEntityManagerType });
            if (_brioEntityManagerInstance == null) return;

            _brioEntityIdType = brioAssembly.GetType("Brio.Entities.Core.EntityId");
            _brioEntityType = brioAssembly.GetType("Brio.Entities.Core.Entity");
            _brioActorEntityType = brioAssembly.GetType("Brio.Entities.Actor.ActorEntity");

            if (_brioEntityIdType == null || _brioEntityType == null || _brioActorEntityType == null)
            {
                PluginLog.Error(
                    "[Brio IPC] Could not find one or more required Brio entity types. Brio may have updated.");
                ResetBrioIpcState();
                _brioSearched = true;
                return;
            }

            _brioEntityIdConstructor = _brioEntityIdType.GetConstructor(new[] { typeof(string) });
            _brioTryGetEntityMethod = _brioEntityManagerType.GetMethod("TryGetEntity",
                new[] { _brioEntityIdType, _brioEntityType.MakeByRefType() });
            _brioActorEntityRawNameField = _brioActorEntityType.GetField("RawName");

            if (_brioEntityIdConstructor == null || _brioTryGetEntityMethod == null ||
                _brioActorEntityRawNameField == null)
            {
                PluginLog.Error("[Brio IPC] Could not find one or more required Brio members. Brio may have updated.");
                ResetBrioIpcState();
                _brioSearched = true;
                return;
            }

            PluginLog.Information("Brio IPC initialized successfully.");
            _brioSearched = true;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[Brio IPC] An exception occurred during initialization:\n{ex}");
            ResetBrioIpcState();
            _brioSearched = true;
        }
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        PluginLog.Information($"[Brio IPC] Plugin state changed: {wasAvailable} -> {isAvailable}. Resetting cache.");
        ResetBrioIpcState();
        _brioSearched = false; // Allow re-initialization
    }

    private void ResetBrioIpcState()
    {
        _brioServicesField = null;
        _brioGetServiceMethod = null;
        _brioEntityManagerType = null;
        _brioEntityManagerInstance = null;
        _brioEntityIdConstructor = null;
        _brioTryGetEntityMethod = null;
        _brioActorEntityRawNameField = null;
        _brioActorEntityType = null;
        _brioEntityIdType = null;
        _brioEntityType = null;
    }
}