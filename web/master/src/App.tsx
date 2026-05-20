import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { AuthCallbackPage } from './pages/AuthCallbackPage';
import { isLoggedIn } from './lib/api';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
    if (!isLoggedIn()) return <Navigate to="/" replace />;
    return <>{children}</>;
}

function App() {
    return (
        <BrowserRouter basename="/app">
            <Routes>
                <Route path="/" element={isLoggedIn() ? <Navigate to="/dashboard" replace /> : <LoginPage />} />
                <Route path="/auth" element={<AuthCallbackPage />} />
                <Route
                    path="/dashboard"
                    element={
                        <ProtectedRoute>
                            <DashboardPage />
                        </ProtectedRoute>
                    }
                />
                <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
        </BrowserRouter>
    );
}

export default App;