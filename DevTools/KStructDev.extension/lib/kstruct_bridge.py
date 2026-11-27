# -*- coding: utf-8 -*-
from System import AppDomain
from System.Reflection import BindingFlags

def status():
    assemblies = []
    for a in AppDomain.CurrentDomain.GetAssemblies():
        n = a.GetName()
        if n.Name != "KStruct":
            continue
        entry = {"name": str(n.Name), "version": str(n.Version), "bridge_available": False}
        try:
            t = a.GetType("SDK_HD.KStruct.Diagnostics.DevToolsBridge", False)
            if t is not None:
                flags = BindingFlags.Public | BindingFlags.Static
                entry["api_version"] = str(t.GetMethod("ApiVersion", flags).Invoke(None, None))
                entry["windows"] = [str(x) for x in t.GetMethod("WindowSnapshot", flags).Invoke(None, None)]
                entry["bridge_available"] = True
        except Exception as ex:
            entry["bridge_error"] = str(ex)
        assemblies.append(entry)
    return {"loaded": bool(assemblies), "assemblies": assemblies}
