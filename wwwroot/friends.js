window.ruleforgeFriends = {
  async overview() {
    const res = await fetch('/api/friends', { credentials: 'include' });
    let data = null; let text = '';
    try { data = await res.json(); } catch { try { text = await res.text(); } catch {} }
    return { ok: res.ok, status: res.status, data, text };
  },
  async request(target) {
    const res = await fetch('/api/friends/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ target })
    });
    let text=''; try { text = await res.text(); } catch {}
    return { ok: res.ok, status: res.status, text };
  },
  async respond(friendLinkId, accept) {
    const res = await fetch('/api/friends/respond', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ friendLinkId, accept })
    });
    let text=''; try { text = await res.text(); } catch {}
    return { ok: res.ok, status: res.status, text };
  }
};
