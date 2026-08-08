use sample_rust_server::db::pool::ConnectionPool;

#[test]
fn test_pool_creates_connection() {
    let pool = ConnectionPool::new("sqlite://test.db", 5);
    let conn = pool.get().unwrap();
    assert!(conn.is_connected());
}

#[test]
fn test_pool_exhaustion() {
    let pool = ConnectionPool::new("sqlite://test.db", 2);
    let _c1 = pool.get().unwrap();
    let _c2 = pool.get().unwrap();
    assert!(pool.get().is_err());
}

#[test]
fn test_return_connection_decrements_active() {
    let pool = ConnectionPool::new("sqlite://test.db", 3);
    let conn = pool.get().unwrap();
    assert_eq!(pool.active_count(), 1);
    pool.return_connection(conn);
    assert_eq!(pool.active_count(), 0);
}

#[test]
fn test_pooled_count_after_return() {
    let pool = ConnectionPool::new("sqlite://test.db", 3);
    let conn = pool.get().unwrap();
    pool.return_connection(conn);
    assert_eq!(pool.pooled_count(), 1);
    // BUG: get() should reuse this pooled connection but doesn't
}
