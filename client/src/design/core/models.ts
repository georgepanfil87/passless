export type CeremonyState =
  | 'idle'
  | 'waiting'      // navigator.credentials.get() pending — user is touching a sensor
  | 'verifying'    // assertion received, server-side checks running
  | 'error'
  | 'unsupported';

export type CeremonyError =
  | 'unrecognised'
  | 'cancelled'
  | 'timeout';

export type AuthenticatorKind = 'platform' | 'cross-platform';
export type Backup = 'synced' | 'device-bound';

export interface Passkey {
  id: string;
  deviceName: string;
  authenticator: string;      // 'Touch ID', 'Security key'
  kind: AuthenticatorKind;
  backup: Backup;
  createdAt: string;          // ISO
  lastUsedLabel: string;
  isCurrentDevice: boolean;
  aaguidLabel?: string;
}

export interface Session {
  id: string;
  device: string;
  browser: string;
  location: string;
  ip: string;
  lastActivityLabel: string;
  method: 'passkey' | 'password' | 'email-link';
  isCurrent: boolean;
}

export type Severity = 'info' | 'notice' | 'critical';

export interface SecurityEvent {
  id: string;
  severity: Severity;
  title: string;
  detail: string;
  /** Convention: [eventId, context]. Rendered as monospace metadata. */
  meta: string[];
  timeLabel: string;
}
