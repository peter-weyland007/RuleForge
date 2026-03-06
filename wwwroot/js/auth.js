window.ruleforgeAuth = {
  async login(identifier, password) {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ identifier, password })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Login failed');
    return text ? JSON.parse(text) : {};
  },

  async register(email, username, password) {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ email, username, password })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Registration failed');
    return text ? JSON.parse(text) : {};
  },

  async me() {
    const res = await fetch('/api/auth/me', { credentials: 'include' });
    if (res.status === 401) return null;
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to fetch profile');
    return text ? JSON.parse(text) : null;
  },

  async logout() {
    const res = await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Logout failed');
    return text ? JSON.parse(text) : {};
  },

  async changePassword(currentPassword, newPassword) {
    const res = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ currentPassword, newPassword })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Password change failed');
    return text ? JSON.parse(text) : {};
  },

  async adminUsers() {
    const res = await fetch('/api/admin/users', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load users');
    return text ? JSON.parse(text) : [];
  },

  async adminUser(id) {
    const res = await fetch(`/api/admin/users/${id}`, { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load user');
    return text ? JSON.parse(text) : null;
  },

  async adminCreateUser(payload) {
    const res = await fetch('/api/admin/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to create user');
    return text ? JSON.parse(text) : {};
  },

  async adminUpdateUser(id, payload) {
    const res = await fetch(`/api/admin/users/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to update user');
    return text ? JSON.parse(text) : {};
  }
,

  async campaignsAccessible() {
    const res = await fetch('/api/campaigns/accessible', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load accessible campaigns');
    return text ? JSON.parse(text) : [];
  },

  async campaignsList() {
    const res = await fetch('/api/campaigns', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load campaigns');
    return text ? JSON.parse(text) : [];
  },

  async campaignGet(id) {
    const res = await fetch(`/api/campaigns/${id}`, { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load campaign');
    return text ? JSON.parse(text) : null;
  },

  async campaignCreate(payload) {
    const res = await fetch('/api/campaigns', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to create campaign');
    return text ? JSON.parse(text) : {};
  },

  async campaignUpdate(id, payload) {
    const res = await fetch(`/api/campaigns/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to update campaign');
    return text ? JSON.parse(text) : {};
  }
,

  async itemCreate(payload) {
    const res = await fetch('/api/items', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || `Create failed (${res.status})`);
    return text ? JSON.parse(text) : {};
  },

  async itemUpdate(id, payload) {
    const res = await fetch(`/api/items/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || `Update failed (${res.status})`);
    return text ? JSON.parse(text) : {};
  }
,

  async friendRequestSend(toUsername) {
    const res = await fetch('/api/friends/requests', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ toUsername })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to send request');
    return text ? JSON.parse(text) : {};
  },

  async friendRequestsIncoming() {
    const res = await fetch('/api/friends/requests/incoming', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load incoming requests');
    return text ? JSON.parse(text) : [];
  },

  async friendRequestApprove(id) {
    const res = await fetch(`/api/friends/requests/${id}/approve`, { method: 'POST', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to approve request');
    return text ? JSON.parse(text) : {};
  },

  async friendRequestDecline(id) {
    const res = await fetch(`/api/friends/requests/${id}/decline`, { method: 'POST', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to decline request');
    return text ? JSON.parse(text) : {};
  },

  async friendsList() {
    const res = await fetch('/api/friends', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load friends');
    return text ? JSON.parse(text) : [];
  }
,

  async adminDeletedGameSystems() {
    const res = await fetch('/api/admin/game-systems/deleted', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load deleted game systems');
    return text ? JSON.parse(text) : [];
  },

  async adminPurgeGameSystem(id) {
    const res = await fetch(`/api/admin/game-systems/${id}/purge`, { method: 'DELETE', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to purge game system');
    return text ? JSON.parse(text) : {};
  }
,
  async featureKanbanList() {
    const res = await fetch('/api/admin/feature-requests', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load feature requests');
    return text ? JSON.parse(text) : [];
  },

  async featureKanbanCreate(payload) {
    const res = await fetch('/api/admin/feature-requests', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to create feature request');
    return text ? JSON.parse(text) : {};
  },

  async featureKanbanUpdate(id, payload) {
    const res = await fetch(`/api/admin/feature-requests/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to update feature request');
    return text ? JSON.parse(text) : {};
  },

  async featureKanbanDelete(id) {
    const res = await fetch(`/api/admin/feature-requests/${id}`, { method: 'DELETE', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to delete feature request');
    return text ? JSON.parse(text) : {};
  }

};
