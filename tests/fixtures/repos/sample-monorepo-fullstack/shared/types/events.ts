export interface AuthEvent {
  type: 'login' | 'logout' | 'refresh' | 'token_expired';
  userId: string;
  timestamp: number;
}

export interface SyncEvent {
  type: 'create' | 'update' | 'delete';
  entity: string;
  entityId: string;
  userId: string;
  timestamp: number;
}
