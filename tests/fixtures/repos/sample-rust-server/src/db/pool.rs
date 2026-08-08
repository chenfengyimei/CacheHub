use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::collections::VecDeque;
use std::sync::Mutex;

use super::connection::Connection;

/// Database connection pool.
/// BUG: Does not implement connection pooling — creates a new connection
/// for every request instead of reusing pooled connections.
pub struct ConnectionPool {
    url: String,
    max_size: usize,
    active: AtomicUsize,
    // BUG: pool is never populated — connections aren't returned to it
    pool: Mutex<VecDeque<Connection>>,
}

impl ConnectionPool {
    pub fn new(url: &str, max_size: usize) -> Self {
        ConnectionPool {
            url: url.to_string(),
            max_size,
            active: AtomicUsize::new(0),
            pool: Mutex::new(VecDeque::new()),
        }
    }

    pub fn get(&self) -> Result<Connection, PoolError> {
        // BUG: Should check pool first, but doesn't
        let current = self.active.load(Ordering::SeqCst);
        if current >= self.max_size {
            return Err(PoolError::PoolExhausted);
        }

        self.active.fetch_add(1, Ordering::SeqCst);
        // BUG: Creates new connection instead of reusing pooled one
        Connection::connect(&self.url).map_err(PoolError::ConnectionError)
    }

    pub fn return_connection(&self, conn: Connection) {
        // BUG: Pushes to pool but get() never reads from it
        if let Ok(mut pool) = self.pool.lock() {
            pool.push_back(conn);
        }
        self.active.fetch_sub(1, Ordering::SeqCst);
    }

    pub fn active_count(&self) -> usize {
        self.active.load(Ordering::SeqCst)
    }

    pub fn pooled_count(&self) -> usize {
        self.pool.lock().map(|p| p.len()).unwrap_or(0)
    }
}

#[derive(Debug)]
pub enum PoolError {
    PoolExhausted,
    ConnectionError(String),
}
