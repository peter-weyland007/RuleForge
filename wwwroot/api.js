window.ruleforgeApi = {
  async send(method, url, body) {
    const opts = { method, credentials: 'include', headers: {} };
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    const res = await fetch(url, opts);
    let data = null, text = '';
    const ct = (res.headers.get('content-type') || '').toLowerCase();
    if (ct.includes('application/json')) {
      try { data = await res.json(); } catch {}
    } else {
      try { text = await res.text(); } catch {}
    }

    if (!res.ok && data !== null && data !== undefined) {
      if (!text && typeof data === 'string') {
        text = data;
      } else if (!text && typeof data === 'object') {
        text = data.message || data.Message || data.title || data.error || '';
      }
      // prevent typed C# interop deserialization failures on error envelopes
      data = null;
    }

    let traceId = null;
    if (data && typeof data === 'object') {
      traceId = data.traceId || data.TraceId || null;
    }
    return { ok: res.ok, status: res.status, data, text, traceId };
  },
  get(url) { return this.send('GET', url); },
  post(url, body) { return this.send('POST', url, body); },
  put(url, body) { return this.send('PUT', url, body); },
  delete(url) { return this.send('DELETE', url); }
};

window.ruleforgePrintElement = (elementId, title) => {
  const source = document.getElementById(elementId);
  if (!source) throw new Error(`Print target not found: ${elementId}`);

  const printWindow = window.open('', '_blank', 'width=900,height=1100');
  if (!printWindow) throw new Error('Popup blocked. Allow popups to print the statblock.');

  const content = source.innerHTML;
  const safeTitle = title || 'Print';

  printWindow.document.open();
  printWindow.document.write(`<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>${safeTitle}</title>
  <style>
    body { font-family: Arial, Helvetica, sans-serif; margin: 24px; color: #111; background: #fff; }
    h1, h2, h3, h4, h5, h6, p { margin-top: 0; }
    .mud-typography { margin: 0; }
    .rf-statblock {
      background:
        radial-gradient(circle at top left, rgba(255,255,255,0.65), transparent 28%),
        radial-gradient(circle at bottom right, rgba(120,72,28,0.08), transparent 30%),
        linear-gradient(180deg, #f8f1dd 0%, #f3e7c7 100%);
      border: 2px solid #2f2418;
      box-shadow: inset 0 0 0 1px rgba(117, 73, 34, 0.22);
      padding: 18px;
      color: #2a2018;
      font-family: Georgia, "Times New Roman", serif;
    }
    .rf-statblock-header {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: flex-start;
      border-bottom: 4px solid #9f2d20;
      padding-bottom: 10px;
      margin-bottom: 14px;
    }
    .rf-statblock-name {
      font-size: 2rem;
      font-weight: 700;
      letter-spacing: 0.03em;
      color: #8b2f1c;
      text-transform: uppercase;
      line-height: 1;
    }
    .rf-statblock-subtitle { font-style: italic; margin-top: 6px; font-size: 1rem; }
    .rf-statblock-meta { text-align: right; font-size: 1rem; white-space: nowrap; }
    .rf-statblock-body { display: grid; grid-template-columns: minmax(260px, 0.95fr) minmax(320px, 1.05fr); gap: 18px; }
    .rf-statblock-divider { border-top: 3px solid #9f2d20; margin: 10px 0 12px; }
    .rf-statblock-line { margin: 3px 0; font-size: 1.02rem; }
    .rf-statblock-line strong { color: #5e180f; }
    .rf-ability-grid { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 8px; text-align: center; }
    .rf-ability-cell { padding: 6px 2px; }
    .rf-ability-label { font-weight: 700; color: #5e180f; letter-spacing: 0.04em; }
    .rf-section-title { color: #8b2f1c; font-size: 1.15rem; font-weight: 700; border-bottom: 1px solid rgba(139,47,28,0.55); padding-bottom: 4px; margin-bottom: 10px; }
    .rf-entry { margin-bottom: 12px; line-height: 1.5; font-size: 1rem; }
    .rf-entry strong { font-style: italic; color: #2a2018; }
    .rf-description-block { margin-top: 18px; }
    .rf-description-title { font-size: 1.6rem; font-weight: 700; margin-bottom: 10px; color: #201712; }
    @media print { body { margin: 12px; } }
  </style>
</head>
<body>
  ${content}
</body>
</html>`);
  printWindow.document.close();
  printWindow.focus();
  printWindow.print();
};
