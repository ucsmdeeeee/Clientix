import axios from 'axios';

const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api';

export const api = axios.create({
    baseURL: API_BASE,
    timeout: 15000,
});

api.interceptors.request.use((config) => {
    const token = localStorage.getItem('jwt');
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

api.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response?.status === 401) {
            localStorage.removeItem('jwt');
            localStorage.removeItem('user');
            if (window.location.pathname !== '/' && !window.location.pathname.endsWith('/app/')) {
                window.location.href = '/app/';
            }
        }
        return Promise.reject(error);
    }
);

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
    reminderDayBefore?: boolean;
    reminderExtraHours?: number;
}

export interface BookingStats {
    total: number;
    completed: number;
    noShow: number;
    cancelledByClient: number;
    cancelledByMaster: number;
    upcoming: number;
    revenueRub: number;
}

export interface MasterStats {
    today: BookingStats;
    week: BookingStats;
    month: BookingStats;
}

export interface DailyStat {
    date: string;
    count: number;
}

// Admin типы
export interface AdminDashboard {
    totalMasters: number;
    activeBots: number;
    paying: number;
    trial: number;
    totalBookings30d: number;
    completedBookings30d: number;
    revenue30d: number;
}

export interface AdminMaster {
    id: number;
    telegramId: number;
    firstName?: string;
    username?: string;
    city?: string;
    niche?: string;
    botUsername?: string;
    isActive: boolean;
    subscriptionStatus: string;
    createdAt: string;
}

export const authApi = {
    loginWithTelegram: (data: TelegramLoginData) =>
        api.post<AuthResponse>('/auth/telegram', data),
};

export const masterApi = {
    getMe: () => api.get<MasterMe>('/master/me'),
    getStats: () => api.get<MasterStats>('/master/stats'),
    getDailyStats: () => api.get<DailyStat[]>('/master/stats/daily'),
};

export const adminApi = {
    getDashboard: () => api.get<AdminDashboard>('/admin/dashboard'),
    getMasters: () => api.get<AdminMaster[]>('/admin/masters'),
};

export const isLoggedIn = () => !!localStorage.getItem('jwt');
export const isAdmin = () => {
    const u = getUser();
    return u?.isAdmin === true;
};

export const logout = () => {
    localStorage.removeItem('jwt');
    localStorage.removeItem('user');
    window.location.href = '/app/';
};

export const getUser = (): AuthResponse | null => {
    const raw = localStorage.getItem('user');
    return raw ? JSON.parse(raw) : null;
};