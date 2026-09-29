(function (root, factory) {
  const api = factory();
  if (typeof module !== "undefined") module.exports = api;
  root.CancerLabStorageAdapter = api;
})(globalThis, function () {
  function completeTransaction(transaction) {
    return new Promise((resolve, reject) => {
      transaction.oncomplete = resolve;
      transaction.onerror = () => reject(transaction.error || new Error("IndexedDB transaction failed"));
      transaction.onabort = () => reject(transaction.error || new Error("IndexedDB transaction aborted"));
    });
  }

  function create({ openDatabase, completeTransaction: waitForTransaction = completeTransaction, notify }) {
    const tails = new Map();
    const enqueue = (key, operation) => {
      const previous = tails.get(key) || Promise.resolve();
      const current = previous.catch(() => {}).then(operation);
      tails.set(key, current.catch(() => {}));
      return current;
    };
    const keyFor = record => `${record.level}:${record.id}`;
    return {
      save(record) {
        return enqueue(keyFor(record), async () => {
          notify({ kind: "storage-status", level: record.level, status: "pending" });
          try {
            const database = await openDatabase();
            const transaction = database.transaction(record.level, "readwrite");
            transaction.objectStore(record.level).put({ ...record, status: "committed", updatedAt: Date.now() });
            await waitForTransaction(transaction);
            notify({ kind: "storage-status", level: record.level, status: "committed" });
          } catch (error) {
            notify({ kind: "storage-status", level: record.level, status: "failed" });
            throw error;
          }
        });
      },
      delete(record) {
        return enqueue(keyFor(record), async () => {
          try {
            const database = await openDatabase();
            const transaction = database.transaction(record.level, "readwrite");
            transaction.objectStore(record.level).delete(record.id);
            await waitForTransaction(transaction);
            notify({ kind: "storage-status", level: record.level, status: "committed" });
          } catch (error) {
            notify({ kind: "storage-status", level: record.level, status: "failed" });
            throw error;
          }
        });
      },
    };
  }
  return { create, completeTransaction };
});
