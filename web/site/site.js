(function () {
  const status = document.getElementById("connection-status");
  const show = message => { status.textContent = message; };
  const onlineMessage = () => navigator.onLine
    ? "This browser is online. The complete local cache is being checked when supported."
    : "This browser reports that it is offline. A previously completed local cache may still open the training station.";

  show(onlineMessage());
  window.addEventListener("online", () => show(onlineMessage()));
  window.addEventListener("offline", () => show(onlineMessage()));
  if (!("serviceWorker" in navigator)) {
    show("This browser does not support the offline cache. The training station can still be opened while online.");
    return;
  }

  const requestCacheStatus = registration => {
    const worker = registration.active || registration.waiting || registration.installing;
    if (!worker) return;
    const channel = new MessageChannel();
    channel.port1.onmessage = event => updateCacheStatus(event.data);
    worker.postMessage({ kind: "cache-status" }, [channel.port2]);
  };
  const updateCacheStatus = message => {
    if (!message || message.kind === "cache-incomplete") {
      show("The offline cache is not complete yet. Keep this page open while online, then check again before relying on offline use.");
    } else if (message.kind === "cache-complete") {
      show("The complete local cache for this release is ready. Browser storage can still be cleared or evicted.");
    } else if (message.kind === "cache-failed" || message.kind === "cache-redundant") {
      show("The offline cache could not complete. The training station remains available while online.");
    }
  };
  navigator.serviceWorker.addEventListener("message", event => updateCacheStatus(event.data));
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("./sw.js", { scope: "./" })
      .then(registration => {
        show("The browser accepted the offline-cache request. Checking whether every release file is cached…");
        requestCacheStatus(registration);
        registration.addEventListener("updatefound", () => {
          const worker = registration.installing;
          if (worker) worker.addEventListener("statechange", () => {
            if (worker.state === "activated") requestCacheStatus(registration);
            if (worker.state === "redundant") updateCacheStatus({ kind: "cache-redundant" });
          });
        });
        navigator.serviceWorker.ready.then(requestCacheStatus);
      })
      .catch(() => updateCacheStatus({ kind: "cache-failed" }));
  });
})();
