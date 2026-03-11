window.ruleforgeAuth = {
  async register(username, email, password) {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ username, email, password })
    });
    let data = null;
    let text = '';
    try { data = await res.json(); } catch {}
    if (!data) { try { text = await res.text(); } catch {} }
    return { ok: res.ok, status: res.status, data, text };
  },

  async login(username, password) {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ username, password })
    });
    let data = null;
    try { data = await res.json(); } catch {}
    return { ok: res.ok, status: res.status, data };
  },

  async changePassword(username, currentPassword, newPassword) {
    const res = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ username, currentPassword, newPassword })
    });
    let text = '';
    try { text = await res.text(); } catch {}
    return { ok: res.ok, status: res.status, text };
  },

  async logout() {
    const res = await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'include'
    });
    return { ok: res.ok, status: res.status };
  }
};
