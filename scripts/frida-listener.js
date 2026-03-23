console.log("[PulseAPK Listener] Frida gadget listener script loaded");
console.log("[PulseAPK Listener] Waiting for Frida client interaction via rpc.exports and message channel");

rpc.exports = {
    ping() {
        const response = "[PulseAPK Listener] pong - client link is alive";
        console.log(response);
        return response;
    },

    run(command) {
        const normalized = (command || "").toString().trim();
        console.log(`[PulseAPK Listener] rpc run() received command: ${normalized || "<empty>"}`);

        if (normalized.length === 0) {
            return "[PulseAPK Listener] No command provided";
        }

        if (normalized === "java-threads") {
            Java.perform(() => {
                const Thread = Java.use("java.lang.Thread");
                const current = Thread.currentThread();
                console.log("[PulseAPK Listener] Current Java thread: " + current.getName());
            });
            return "[PulseAPK Listener] java-threads command executed";
        }

        if (normalized === "version") {
            return "[PulseAPK Listener] PulseAPK Frida listener profile active";
        }

        return `[PulseAPK Listener] Unknown command: ${normalized}`;
    }
};

(function setupInboundChannel() {
    const channel = "pulseapk:command";

    function waitNext() {
        recv(channel, function onMessage(message) {
            const payload = message && message.payload !== undefined ? message.payload : message;
            console.log("[PulseAPK Listener] message channel payload: " + JSON.stringify(payload));
            send({
                type: "pulseapk:ack",
                status: "received",
                payload
            });
            waitNext();
        });
    }

    waitNext();
    console.log(`[PulseAPK Listener] Subscribed to message channel '${channel}'`);
})();
