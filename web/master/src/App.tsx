import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { AdminPage } from './pages/AdminPage';
import { AuthCallbackPage } from './pages/AuthCallbackPage';
import { isLoggedIn, isAdmin } from './lib/api';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
    if (!isLoggedIn()) return <Navigate to="/" replace />;
    return <>{children}</>;
}

function AdminRoute({ children }: { children: React.ReactNode }) {
    if (!isLoggedIn()) return <Navigate to="/" replace />;
    if (!isAdmin()) return <Navigate to="/dashboard" replace />;
    return <>{children}</>;
}

function HomePage() {
    if (!isLoggedIn()) return <LoginPage />;
    if (isAdmin()) return <Navigate to="/admin" replace />;
    return <Navigate to="/dashboard" replace />;
}

function App() {
    return (
        <BrowserRouter basename="/app">
            <Routes>
                <Route path="/" element={<HomePage />} />
                <Route path="/auth" element={<AuthCallbackPage />} />
                <Route
                    path="/dashboard"
                    element={
                        <ProtectedRoute>
                            <DashboardPage />
                        </ProtectedRoute>
                    }
                />
                <Route
                    path="/admin"
                    element={
                        <AdminRoute>
                            <AdminPage />
                        </AdminRoute>
                    }
                />
                <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
        </BrowserRouter>
    );
}

export default App;