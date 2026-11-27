# -*- coding: utf-8 -*-
"""Toggle opt-in capture and show the most recent closed KStruct dialog."""
import json
from pyrevit import forms
from kstruct_bridge import set_enabled, snapshot

choice = forms.CommandSwitchWindow.show(
    ["Enable capture", "Show last capture", "Disable capture"],
    message="KStruct WPF diagnostics (counts only; no input values)")
try:
    if choice == "Enable capture":
        set_enabled(True)
        forms.alert("Capture enabled. Open and close a KStruct rebar dialog, then show the last capture.")
    elif choice == "Disable capture":
        set_enabled(False)
        forms.alert("Capture disabled.")
    elif choice == "Show last capture":
        data = snapshot()
        if data is None:
            forms.alert("No completed capture in this Revit session.")
        else:
            print(json.dumps(data, indent=2))
except Exception as error:
    forms.alert("WPF diagnostics unavailable: %s" % error)
