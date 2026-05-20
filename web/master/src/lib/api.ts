import axios from 'axios';

const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api';

export const api = axios.create({
    baseURL: API_BASE,
    timeout: 15000,
});

// Автоматически добавляем JWT в каждый запрос
api.interceptors.request.use((config) => {
    const token = localStorage.getItem('jwt');
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

// При 401 — кикаем на логин
api.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response?.status === 401) {
            localStorage.removeItem('jwt');
            localStorage.removeItem('user');
            if (window.location.pathname !== '/') {
                window.location.href = '/';
            }
        }
        return Promise.reject(error);
    }
);

// Типы
export interface TelegramLoginData {
    id: number;
    first_name?: string;
    last_name?: string;
    username?: string;
    photo_url?: string;
    auth_date: number;
    hash: string;
}

export interface AuthResponse {
    token: string;
    userId: number;
    telegramId: number;
    firstName?: string;
    username?: string;
    isAdmin: boolean;
}

export interface MasterMe {
    id: number;
    telegramId: number;
    firstName?: string;
    lastName?: string;
    username?: string;
    city?: string;
    niche?: string;
    botUsername?: string;
    subscriptionStatus?: string;
    timezone?: string;
}

export interface DayStats {
    count: number;
    completed: number;
    cancelled: number;
    noShow: number;
    revenue: number;
}

export interface MasterStats {
    today: DayStats;
    week: DayStats;
    month: DayStats;
}

// API методы
export const authApi = {
    loginWithTelegram: (data: TelegramLoginData) =>
        api.post<AuthResponse>('/auth/telegram', data),
};

export const masterApi = {
    getMe: () => api.get<MasterMe>('/master/me'),
    getStats: () => api.get<MasterStats>('/master/stats'),
};

// Helper
export const isLoggedIn = () => !!localStorage.getItem('jwt');

export const logout = () => {
    localStorage.removeItem('jwt');
    localStorage.removeItem('user');
    window.location.href = '/';
};

export const getUser = (): AuthResponse | null => {
    const raw = localStorage.getItem('user');
    return raw ? JSON.parse(raw) : null;
};