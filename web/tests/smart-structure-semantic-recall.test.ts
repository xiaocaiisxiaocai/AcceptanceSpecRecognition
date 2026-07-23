import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

test("智能结构确认卡应展示列语义召回建议", () => {
  const apiSource = readProjectFile("web/src/api/smart-config.ts");
  const cardSource = readProjectFile(
    "web/src/views/shared/SmartStructureConfirmCard.vue"
  );

  assert.match(apiSource, /SmartConfigColumnSemanticRecallSuggestion/);
  assert.match(apiSource, /semanticRecallSuggestions\?/);
  assert.match(cardSource, /semanticRecallSuggestions/);
  assert.match(cardSource, /语义召回建议/);
  assert.match(cardSource, /SemanticRecall/);
});
