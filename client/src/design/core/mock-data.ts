import { Passkey, SecurityEvent, Session } from './models';

/* Mock data only. There is no HttpClient anywhere in this layer, by design:
   the UI is being reviewed before any of it is wired to the API. */

export const MOCK_PASSKEYS: Passkey[] = [
  { id: 'pk_1', deviceName: "Rae's MacBook Pro", authenticator: 'Touch ID',
    kind: 'platform', backup: 'synced', createdAt: '2026-03-14',
    lastUsedLabel: '2 min ago', isCurrentDevice: true },
  { id: 'pk_2', deviceName: 'iPhone 15 Pro', authenticator: 'Face ID',
    kind: 'platform', backup: 'synced', createdAt: '2026-03-14',
    lastUsedLabel: 'Yesterday', isCurrentDevice: false },
  { id: 'pk_3', deviceName: 'YubiKey 5C NFC', authenticator: 'Security key',
    kind: 'cross-platform', backup: 'device-bound', createdAt: '2025-11-02',
    lastUsedLabel: '18 Aug 2026', isCurrentDevice: false,
    aaguidLabel: 'AAGUID cb69481e…' },
];

export const MOCK_SESSIONS: Session[] = [
  { id: 's_1', device: 'MacBook Pro', browser: 'Chrome 141', location: 'Lisbon, PT',
    ip: '88.12.4.201', lastActivityLabel: 'Active now', method: 'passkey', isCurrent: true },
  { id: 's_2', device: 'iPhone 15 Pro', browser: 'Safari', location: 'Lisbon, PT',
    ip: 'mobile', lastActivityLabel: '3 h ago', method: 'passkey', isCurrent: false },
  { id: 's_3', device: 'Linux', browser: 'Firefox 114', location: 'Frankfurt, DE',
    ip: '45.9.61.7', lastActivityLabel: '2 days ago', method: 'email-link', isCurrent: false },
  { id: 's_4', device: 'iPad Air', browser: 'Safari', location: 'Porto, PT',
    ip: '2.82.14.9', lastActivityLabel: '11 days ago', method: 'passkey', isCurrent: false },
];

export const MOCK_EVENTS: SecurityEvent[] = [
  { id: 'evt_9f31c0', severity: 'critical', title: 'Refresh token reuse detected',
    detail: 'A token already rotated at 09:14 was presented again from 45.9.61.7 (Frankfurt, DE). The session family was revoked and all passkeys were left intact.',
    meta: ['evt_9f31c0', 'Firefox 114 · Linux'], timeLabel: '09:16 today' },
  { id: 'evt_9f2ab4', severity: 'notice', title: 'Session revoked',
    detail: 'You signed out iPad Air · Safari from MacBook Pro · Chrome 141.',
    meta: ['evt_9f2ab4', 'Lisbon, PT'], timeLabel: 'Yesterday 18:40' },
  { id: 'evt_9e77d1', severity: 'info', title: 'Passkey added',
    detail: "Rae's MacBook Pro · Touch ID, synced through iCloud Keychain.",
    meta: ['evt_9e77d1', 'ES256'], timeLabel: '14 Mar 11:02' },
  { id: 'evt_9e10aa', severity: 'notice', title: 'Password sign-in used',
    detail: 'Fallback path used on Linux · Firefox 114, where no authenticator was available.',
    meta: ['evt_9e10aa', 'Frankfurt, DE'], timeLabel: '02 Mar 07:31' },
];
