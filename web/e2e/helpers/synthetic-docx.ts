const encoder = new TextEncoder();

function crc32(bytes: Uint8Array) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function littleEndian(value: number, size: number) {
  const bytes = new Uint8Array(size);
  const view = new DataView(bytes.buffer);
  if (size === 2) view.setUint16(0, value, true);
  else view.setUint32(0, value, true);
  return bytes;
}

function concat(parts: Uint8Array[]) {
  const result = new Uint8Array(
    parts.reduce((sum, part) => sum + part.length, 0)
  );
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }
  return result;
}

function storedZip(entries: Array<{ name: string; content: string }>) {
  const localParts: Uint8Array[] = [];
  const centralParts: Uint8Array[] = [];
  let offset = 0;

  for (const entry of entries) {
    const name = encoder.encode(entry.name);
    const data = encoder.encode(entry.content);
    const checksum = crc32(data);
    const local = concat([
      littleEndian(0x04034b50, 4),
      littleEndian(20, 2),
      littleEndian(0, 2),
      littleEndian(0, 2),
      littleEndian(0, 2),
      littleEndian(0, 2),
      littleEndian(checksum, 4),
      littleEndian(data.length, 4),
      littleEndian(data.length, 4),
      littleEndian(name.length, 2),
      littleEndian(0, 2),
      name,
      data
    ]);
    localParts.push(local);

    centralParts.push(
      concat([
        littleEndian(0x02014b50, 4),
        littleEndian(20, 2),
        littleEndian(20, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(checksum, 4),
        littleEndian(data.length, 4),
        littleEndian(data.length, 4),
        littleEndian(name.length, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(0, 2),
        littleEndian(0, 4),
        littleEndian(offset, 4),
        name
      ])
    );
    offset += local.length;
  }

  const localDirectory = concat(localParts);
  const centralDirectory = concat(centralParts);
  return concat([
    localDirectory,
    centralDirectory,
    littleEndian(0x06054b50, 4),
    littleEndian(0, 2),
    littleEndian(0, 2),
    littleEndian(entries.length, 2),
    littleEndian(entries.length, 2),
    littleEndian(centralDirectory.length, 4),
    littleEndian(localDirectory.length, 4),
    littleEndian(0, 2)
  ]);
}

function xml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

export function createSyntheticDocx(rows: string[][]) {
  const tableRows = rows
    .map(
      row =>
        `<w:tr>${row
          .map(
            cell =>
              `<w:tc><w:tcPr/><w:p><w:r><w:t xml:space="preserve">${xml(cell)}</w:t></w:r></w:p></w:tc>`
          )
          .join("")}</w:tr>`
    )
    .join("");
  const documentXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:tbl><w:tblPr/><w:tblGrid/>${tableRows}</w:tbl><w:sectPr/></w:body></w:document>`;

  return storedZip([
    {
      name: "[Content_Types].xml",
      content:
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'
    },
    {
      name: "_rels/.rels",
      content:
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
    },
    { name: "word/document.xml", content: documentXml }
  ]);
}
