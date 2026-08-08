import { Request, Response, NextFunction } from 'express';

interface LogEntry {
  timestamp: string;
  method: string;
  path: string;
  status: number;
  durationMs: number;
  ip: string;
}

const logEntries: LogEntry[] = [];
const MAX_ENTRIES = 1000;

export function loggerMiddleware(req: Request, res: Response, next: NextFunction): void {
  const start = Date.now();
  const { method, path, ip } = req;

  res.on('finish', () => {
    const durationMs = Date.now() - start;
    const entry: LogEntry = {
      timestamp: new Date().toISOString(),
      method,
      path,
      status: res.statusCode,
      durationMs,
      ip: ip || 'unknown',
    };

    logEntries.push(entry);
    if (logEntries.length > MAX_ENTRIES) {
      logEntries.shift();
    }

    // Console output
    const level = res.statusCode >= 400 ? 'ERROR' : 'INFO';
    console.log(`[${entry.timestamp}] ${level} ${method} ${path} ${res.statusCode} ${durationMs}ms`);
  });

  next();
}

export function getLogEntries(): LogEntry[] {
  return [...logEntries];
}

export function clearLogEntries(): void {
  logEntries.length = 0;
}
