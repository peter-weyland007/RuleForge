window.ruleforgePrefs = {
  async get() {
    try {
      const res = await fetch('/api/me/preferences', { credentials: 'include' });
      if (!res.ok) return null;
      return await res.json();
    } catch { return null; }
  },
  async set(payload) {
    try {
      await fetch('/api/me/preferences', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload)
      });
    } catch {}
  }
};
