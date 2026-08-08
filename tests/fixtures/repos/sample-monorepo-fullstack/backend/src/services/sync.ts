import { Request, Response } from 'express';
import { AuthEvent } from '../../../shared/types/events';

// In-memory event store (would be a DB in production)
const events: AuthEvent[] = [];

export function recordEvent(event: AuthEvent): void {
  events.push(event);
  // Keep last 1000 events
  if (events.length > 1000) {
    events.shift();
  }
}

export function getEvents(userId?: string): AuthEvent[] {
  if (userId) {
    return events.filter(e => e.userId === userId);
  }
  return [...events];
}

export function clearEvents(): void {
  events.length = 0;
}
