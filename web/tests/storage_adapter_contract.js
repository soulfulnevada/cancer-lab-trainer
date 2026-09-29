"use strict";

const assert = require("node:assert/strict");
const { create, completeTransaction } = require("../web-template/storage_adapter.js");

function fakeIndexedDb(records, abortNext) {
  return {
    transaction(level) {
      const pending = new Map(records);
      const transaction = { operation: null };
      transaction.objectStore = () => ({
        put: record => { transaction.operation = () => pending.set(`${level}:${record.id}`, record); },
        delete: id => { transaction.operation = () => pending.delete(`${level}:${id}`); },
      });
      transaction.commit = () => {
        if (abortNext()) throw new Error("simulated transaction abort");
        transaction.operation();
        records.clear();
        for (const [key, value] of pending) records.set(key, value);
      };
      return transaction;
    },
  };
}

(async () => {
  const completed = { oncomplete: null, onabort: null, onerror: null, error: null };
  const completion = completeTransaction(completed);
  completed.oncomplete();
  await completion;
  const aborted = { oncomplete: null, onabort: null, onerror: null, error: new Error("aborted") };
  const abortCompletion = completeTransaction(aborted);
  aborted.onabort();
  await assert.rejects(abortCompletion, /aborted/, "the shared completion helper rejects aborts");

  const records = new Map([["level1:good", { level: "level1", id: "good", save: "old", status: "committed" }]]);
  const statuses = [];
  let abort = true;
  const database = fakeIndexedDb(records, () => abort);
  const adapter = create({
    openDatabase: async () => database,
    completeTransaction: async tx => tx.commit(),
    notify: event => statuses.push(event),
  });
  await assert.rejects(adapter.save({ level: "level1", id: "good", save: "new" }));
  assert.equal(records.get("level1:good").save, "old", "an aborted production transaction preserves the committed resume");
  assert.equal(statuses.at(-1).status, "failed");

  abort = false;
  await Promise.all([
    adapter.save({ level: "level1", id: "latest", save: "one" }),
    adapter.save({ level: "level1", id: "latest", save: "two" }),
  ]);
  assert.equal(records.get("level1:latest").save, "two", "queued production saves keep the newest record");
  const save = adapter.save({ level: "level1", id: "gone", save: "new" });
  const remove = adapter.delete({ level: "level1", id: "gone" });
  await Promise.all([save, remove]);
  assert.equal(records.has("level1:gone"), false, "queued production delete wins over save");
  await Promise.all([
    adapter.save({ level: "level1", id: "same", save: "one" }),
    adapter.save({ level: "level2", id: "same", save: "two" }),
  ]);
  assert.equal(records.get("level1:same").save, "one");
  assert.equal(records.get("level2:same").save, "two", "level stores remain isolated");
  console.log("PASS: production IndexedDB mutation adapter contracts");
})().catch(error => { console.error(error); process.exitCode = 1; });
