# -*- coding: utf-8 -*-
"""Read-only bridge to the loaded KStruct assembly."""
import System
from System import AppDomain
def status():
    assemblies = []
    for a in AppDomain.CurrentDomain.GetAssemblies():
        n = a.GetName()
        if n.Name == "KStruct":
            assemblies.append({"name": str(n.Name), "version": str(n.Version)})
    return {"loaded": bool(assemblies), "assemblies": assemblies}
