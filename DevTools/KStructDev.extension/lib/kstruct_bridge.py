# -*- coding: utf-8 -*-
"""Read-only bridge to the already loaded KStruct assembly; never loads a DLL."""
from System import AppDomain


def _bridge():
    assemblies = [a for a in AppDomain.CurrentDomain.GetAssemblies()
                  if a.GetName().Name == "KStruct"]
    if len(assemblies) != 1:
        raise RuntimeError("Expected exactly one loaded KStruct assembly; found %d." % len(assemblies))
    bridge = assemblies[0].GetType("SDK_HD.KStruct.DevTools.WpfDiagnostics")
    if bridge is None:
        raise RuntimeError("The loaded KStruct DLL does not contain the WPF diagnostics bridge.")
    return bridge


def status():
    assemblies = []
    for a in AppDomain.CurrentDomain.GetAssemblies():
        n = a.GetName()
        if n.Name == "KStruct":
            assemblies.append({"name": str(n.Name), "version": str(n.Version),
                               "bridge_available": a.GetType("SDK_HD.KStruct.DevTools.WpfDiagnostics") is not None})
    return {"loaded": bool(assemblies), "assemblies": assemblies}


def set_enabled(enabled):
    _bridge().GetProperty("Enabled").SetValue(None, bool(enabled), None)


def snapshot():
    bridge = _bridge()
    item = bridge.GetProperty("LastSnapshot").GetValue(None, None)
    if item is None:
        error = bridge.GetProperty("LastError").GetValue(None, None)
        if error:
            raise RuntimeError(str(error))
        return None
    result = {}
    for name in ("Kind", "CapturedUtc", "WidthDip", "HeightDip", "DpiX", "DpiY",
                 "VisualCount", "NamedCount", "ControlCount", "BindingCount", "BindingErrorCount", "CanvasCount", "CanvasChildren",
                 "Viewport3DCount", "Viewport3DChildren", "ValidationErrorCount"):
        value = getattr(item, name)
        result[name] = str(value) if name in ("Kind", "CapturedUtc") else float(value) if name in ("WidthDip", "HeightDip", "DpiX", "DpiY") else int(value)
    result["Events"] = dict((str(kv.Key), int(kv.Value)) for kv in item.Events)
    return result
