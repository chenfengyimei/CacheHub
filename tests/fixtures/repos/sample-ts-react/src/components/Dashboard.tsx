import React, { useState, useEffect } from 'react';
import { useAuth } from '../hooks/useAuth';
import { ErrorBoundary } from './ErrorBoundary';
import { User } from '../types/user';

export function Dashboard() {
  const { user, loading, logout } = useAuth();
  const [profile, setProfile] = useState<User | null>(null);

  useEffect(() => {
    if (user) {
      fetch(`/api/users/${user.id}`)
        .then(res => res.json())
        .then(setProfile)
        .catch(console.error);
    }
  }, [user]);

  if (loading) return <div>Loading...</div>;
  if (!user) return <div>Please log in</div>;

  return (
    <ErrorBoundary>
      <div className="dashboard">
        <h1>Welcome, {user.name}</h1>
        {profile && (
          <div className="profile">
            <p>Email: {profile.email}</p>
            <p>Role: {profile.role}</p>
          </div>
        )}
        <button onClick={logout}>Logout</button>
      </div>
    </ErrorBoundary>
  );
}
