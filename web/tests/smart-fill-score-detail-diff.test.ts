import test from "node:test";
import assert from "node:assert/strict";
import {
  isHintOnlyTextDifference,
  normalizeHintOnlyDiffText
} from "../src/views/smart-fill/composables/useScoreDetailDiff.ts";

test("源项差异归一化应忽略换行、空白和常见标点", () => {
  assert.equal(
    normalizeHintOnlyDiffText("软件规范\n-通讯协议"),
    normalizeHintOnlyDiffText("软件规范-通讯协议")
  );

  assert.equal(
    normalizeHintOnlyDiffText("设备作业面参考工作水平高度 1150 ± 50 mm，整条设备的水平高度上下浮差尽可能控制在靠近中值1150mm；"),
    normalizeHintOnlyDiffText("设备作业面参考工作水平高度 1150 ± 50 mm，整条设备的水平高度上下浮差尽可能控制在靠近中值1150mm")
  );
});

test("仅格式、符号、换行差异应被识别为提示型差异", () => {
  assert.equal(
    isHintOnlyTextDifference("软件规范\n-通讯协议", "软件规范-通讯协议"),
    true
  );

  assert.equal(
    isHintOnlyTextDifference("1150 ± 50 mm；", "1150±50mm"),
    true
  );
});

test("实质文本差异不能被降级为提示型差异", () => {
  assert.equal(
    isHintOnlyTextDifference(
      "PLC: 1、TCP/UDP通讯协议",
      "PLC: 1、TCP/UDP通讯协议；2、MODBUS TCP协议"
    ),
    false
  );
});
