#!/usr/bin/env node
/**
 * Branching from a version — checkout -b, not checkout.
 *
 * Restore moves a marker and writes nothing (api #115). Branching is the other
 * half of that distinction: an old state carried forward as its own document,
 * which is what append-on-restore was pretending to be. The rules it has to
 * keep are not obvious from the code, so they are asserted against a real
 * database:
 *
 *   1. The branch holds the version's content, not the document's current one.
 *   2. Nesting survives — the ids change, so parent links must change with them.
 *   3. No block id is shared with the source. They are a primary key across
 *      every document; sharing one is not a thing that can happen.
 *   4. The source is untouched: no version appended, no marker moved, no block
 *      changed.
 *   5. The branch opens on its own v1, marked current — which only works if
 *      that version was serialised from the branch rather than copied from the
 *      source, because rebasing changed every id.
 *   6. A title is derived when none is given, and an explicit one wins.
 *
 *   API=http://127.0.0.1:5001 node scripts/verify-version-branch.mjs
 */
const API = process.env.API || "http://127.0.0.1:5001";

const log = (ok, msg) => console.log(`  ${ok ? "OK  " : "FAIL"} ${msg}`);
let failures = 0;
const check = (cond, msg) => { if (!cond) failures++; log(cond, msg); };

async function api(path, init = {}) {
    const res = await fetch(`${API}${path}`, {
        ...init,
        headers: { "content-type": "application/json", ...(init.headers || {}) },
    });
    if (!res.ok) throw new Error(`${init.method || "GET"} ${path} → ${res.status}`);
    return res.status === 204 ? null : res.json();
}

const post = (p, body) => api(p, { method: "POST", body: JSON.stringify(body ?? {}) });

const main = async () => {
    console.log(`\nbranch from version — api ${API}\n`);

    const doc = await post("/api/documents", { title: "Wave mechanics" });
    const docId = doc.id ?? doc.documentId;

    const list = await post(`/api/documents/${docId}/blocks`,
        { type: "list", content: { ordered: true }, sortOrder: 0 });
    await post(`/api/documents/${docId}/blocks`,
        { type: "listItem", content: { text: "one" }, sortOrder: 1, parentId: list.id, depth: 1 });

    const version = await post(`/api/documents/${docId}/versions`, { name: "the good one" });
    const snapshotBlocks = version.snapshot.blocks.length;
    check(snapshotBlocks > 0, `v${version.versionNumber} holds ${snapshotBlocks} blocks`);

    // Diverge, so "the version's content" and "the document's content" are
    // different answers and the test can tell which one branching used.
    await post(`/api/documents/${docId}/blocks`,
        { type: "paragraph", content: { text: "written after the version" }, sortOrder: 9 });

    const before = await api(`/api/documents/${docId}`);
    const branch = await post(`/api/documents/${docId}/versions/${version.id}/branch`, {});

    // 1.
    check(
        branch.blocks.length === snapshotBlocks,
        `branch took the version's ${snapshotBlocks} blocks, not the document's ${before.blocks.length}`,
    );
    check(
        !JSON.stringify(branch.blocks).includes("written after the version"),
        "the post-version edit is not in the branch",
    );

    // 2.
    const bList = branch.blocks.find((b) => b.type === "list");
    const bItem = branch.blocks.find((b) => b.type === "listItem");
    check(!!bList && !!bItem, "both nested blocks came across");
    check(bItem?.parentId === bList?.id, "the child points at the branch's own parent");
    check(bItem?.parentId !== list.id, "…not at the source document's parent");
    check(bItem?.depth === 1, "depth survived");

    // 3.
    const sourceIds = new Set(before.blocks.map((b) => b.id));
    check(
        branch.blocks.every((b) => !sourceIds.has(b.id)),
        "no block id is shared with the source",
    );

    // 4.
    const after = await api(`/api/documents/${docId}`);
    const sourceVersions = await api(`/api/documents/${docId}/versions`);
    check(sourceVersions.length === 1, `source still has ${sourceVersions.length} version`);
    check(after.blocks.length === before.blocks.length, "source block count unchanged");
    check(after.title === "Wave mechanics", "source title unchanged");

    // 5.
    const branchVersions = await api(`/api/documents/${branch.id}/versions`);
    check(branchVersions.length === 1, "the branch opens with one version");
    check(
        branchVersions[0]?.isCurrent === true,
        "the branch is marked as sitting on it — the snapshot describes the branch, not the source",
    );
    check(
        branchVersions[0]?.name === `Branched from v${version.versionNumber}`,
        `named "${branchVersions[0]?.name}"`,
    );

    // 6.
    check(branch.title === "Wave mechanics (from v1)", `derived title "${branch.title}"`);
    const named = await post(`/api/documents/${docId}/versions/${version.id}/branch`,
        { title: "Rewrite" });
    check(named.title === "Rewrite", "an explicit title wins");

    const res = await fetch(
        `${API}/api/documents/${docId}/versions/00000000-0000-0000-0000-000000000000/branch`,
        { method: "POST", headers: { "content-type": "application/json" }, body: "{}" });
    check(res.status === 404, `branching an unknown version → ${res.status}`);

    console.log(`\n${failures === 0 ? "PASS" : `FAIL (${failures})`}\n`);
    process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
