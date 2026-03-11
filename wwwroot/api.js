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

    if (!res.ok && !text && data !== null && data !== undefined) {
      if (typeof data === 'string') {
        text = data;
      } else if (typeof data === 'object') {
        text = data.message || data.Message || data.title || data.error || '';
      }
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
