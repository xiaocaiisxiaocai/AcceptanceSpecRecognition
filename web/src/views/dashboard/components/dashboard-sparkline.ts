export interface SparklineGeometry {
  points: string;
  areaPath: string;
}

export const buildSparklineGeometry = (
  values: number[],
  width = 100,
  height = 36,
  verticalPadding = 3
): SparklineGeometry => {
  if (values.length === 0) return { points: "", areaPath: "" };

  const finiteValues = values.map(value =>
    Number.isFinite(value) ? Math.max(0, value) : 0
  );
  const max = Math.max(...finiteValues, 1);
  const drawableHeight = height - verticalPadding * 2;
  const denominator = Math.max(finiteValues.length - 1, 1);
  const coordinates = finiteValues.map((value, index) => ({
    x: (index / denominator) * width,
    y: height - verticalPadding - (value / max) * drawableHeight
  }));
  const points = coordinates
    .map(({ x, y }) => `${x.toFixed(2)},${y.toFixed(2)}`)
    .join(" ");
  const first = coordinates[0];
  const last = coordinates.at(-1)!;

  return {
    points,
    areaPath: `M ${first.x.toFixed(2)} ${height} L ${points.replaceAll(
      ",",
      " "
    )} L ${last.x.toFixed(2)} ${height} Z`
  };
};
