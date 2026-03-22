console.log("[PulseAPK] Frida gadget script starting up");

Java.perform(function () {
    try {
        var hasShownToast = false;
        var Activity = Java.use("android.app.Activity");
        var Toast = Java.use("android.widget.Toast");
        var StringClass = Java.use("java.lang.String");
        var onResume = Activity.onResume.overload();

        onResume.implementation = function () {
            onResume.call(this);

            if (!hasShownToast) {
                hasShownToast = true;
                var activity = this;
                Java.scheduleOnMainThread(function () {
                    Toast.makeText(
                        activity,
                        StringClass.$new("Frida hook active"),
                        Toast.LENGTH_SHORT.value
                    ).show();
                });
            }
        };

        console.log("[PulseAPK] Hooked android.app.Activity.onResume successfully");
    } catch (error) {
        console.error("[PulseAPK] Failed to hook android.app.Activity.onResume:", error);
    }
});
