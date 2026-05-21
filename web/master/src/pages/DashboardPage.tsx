import { motion } from 'framer-motion';
import { useEffect, useState } from 'react';
import { CheckCircle, XCircle, AlertCircle, TrendingUp, LogOut } from 'lucide-react';
import {
    LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid,
} from 'recharts';
import { masterApi, type MasterMe, type MasterStats, logout, getUser } from '../lib/api';
import { Particles } from '../components/Particles';
import { AnimatedCounter } from '../components/AnimatedCounter';
import logo from '../assets/logo.jpg';

const PLATINUM = '#E8E8E8';

export function DashboardPage() {
    const [me, setMe] = useState<MasterMe | null>(null);
    const [stats, setStats] = useState<MasterStats | null>(null);
    const [loading, setLoading] = useState(true);
    const user = getUser();

    useEffect(() => {
        Promise.all([masterApi.getMe(), masterApi.getStats()])
            .then(([meRes, statsRes]) => {
                setMe(meRes.data);
                setStats(statsRes.data);
            })
            .catch((err) => console.error('Dashboard load error', err))
            .finally(() => setLoading(false));
    }, []);

    if (loading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-cx-black">
                <div className="w-10 h-10 border-2 rounded-full animate-spin"
                    style={{ borderColor: '#1F1F26', borderTopColor: PLATINUM }} />
            </div>
        );
    }

    const chartData = generateMockChartData();

    return (
        <div className="relative min-h-screen overflow-hidden"
            style={{ background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)' }}>
            <Particles count={10} />

            <header className="sticky top-0 z-50 border-b backdrop-blur-md"
                style={{ borderColor: '#1F1F26', background: 'rgba(0,0,0,0.8)' }}>
                <div className="max-w-6xl mx-auto px-6 py-4 flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <img src={logo} alt="ClientiX" className="w-10 h-10 rounded-full"
                            style={{ boxShadow: '0 0 15px rgba(232,232,232,0.2)' }} />
                        <div>
                            <h1 className="font-serif text-xl font-light"
                                style={{
                                    background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                    WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                                }}>ClientiX</h1>
                            <p className="text-[10px] tracking-widest uppercase" style={{ color: '#6B6B6B' }}>Кабинет мастера</p>
                        </div>
                    </div>
                    <div className="flex items-center gap-4">
                        <div className="text-right hidden md:block">
                            <p className="text-sm font-medium text-cx-cream">{user?.firstName || me?.firstName || 'Мастер'}</p>
                            {(user?.username || me?.username) && (
                                <p className="text-xs" style={{ color: '#6B6B6B' }}>@{user?.username || me?.username}</p>
                            )}
                        </div>
                        <motion.button whileHover={{ scale: 1.05, borderColor: PLATINUM }} whileTap={{ scale: 0.95 }}
                            onClick={logout} className="p-2 border transition-colors"
                            style={{ borderColor: '#1F1F26', borderRadius: '2px' }} title="Выйти">
                            <LogOut className="w-4 h-4" style={{ color: '#A8A8A8' }} />
                        </motion.button>
                    </div>
                </div>
            </header>

            <main className="relative z-10 max-w-6xl mx-auto px-6 py-12">
                <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6 }} className="mb-12">
                    <h2 className="font-serif text-4xl md:text-5xl font-light text-cx-cream mb-2">
                        Привет,{' '}
                        <span className="italic"
                            style={{
                                background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                            }}>{me?.firstName || 'мастер'}</span>
                    </h2>
                    <p className="text-base font-light" style={{ color: '#A8A8A8' }}>
                        {me?.botUsername ? (
                            <>Твой бот:{' '}
                                <a href={`https://t.me/${me.botUsername}`} target="_blank" rel="noopener"
                                    className="font-medium hover:underline" style={{ color: PLATINUM }}>
                                    @{me.botUsername}
                                </a></>
                        ) : 'Бот пока не подключён'}
                    </p>
                </motion.div>

                <div className="grid md:grid-cols-3 gap-6 mb-12">
                    <PeriodCard title="Сегодня" stats={stats?.today} delay={0.1} />
                    <PeriodCard title="Эта неделя" stats={stats?.week} delay={0.2} />
                    <PeriodCard title="Этот месяц" stats={stats?.month} delay={0.3} highlight />
                </div>

                <motion.div initial={{ opacity: 0, y: 30 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 0.4 }} className="relative p-8 border"
                    style={{
                        background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                        borderColor: '#1F1F26', borderRadius: '2px',
                    }}>
                    <div className="flex items-center justify-between mb-6">
                        <div>
                            <h3 className="font-serif text-2xl font-light text-cx-cream">
                                Записи за{' '}
                                <span className="italic"
                                    style={{
                                        background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                                    }}>30 дней</span>
                            </h3>
                            <p className="text-sm mt-1" style={{ color: '#A8A8A8' }}>Динамика по дням</p>
                        </div>
                        <TrendingUp className="w-6 h-6" style={{ color: PLATINUM }} />
                    </div>
                    <div style={{ height: 300 }}>
                        <ResponsiveContainer width="100%" height="100%">
                            <LineChart data={chartData}>
                                <CartesianGrid strokeDasharray="3 3" stroke="#1F1F26" />
                                <XAxis dataKey="day" stroke="#6B6B6B" fontSize={12}
                                    tickLine={false} axisLine={{ stroke: '#1F1F26' }} />
                                <YAxis stroke="#6B6B6B" fontSize={12}
                                    tickLine={false} axisLine={{ stroke: '#1F1F26' }} />
                                <Tooltip contentStyle={{
                                    background: '#16161A', border: '1px solid #1F1F26',
                                    borderRadius: '2px', color: '#E8E8E8',
                                }} cursor={{ stroke: PLATINUM, strokeOpacity: 0.3 }} />
                                <Line type="monotone" dataKey="count" stroke={PLATINUM} strokeWidth={2}
                                    dot={{ fill: PLATINUM, r: 3 }} activeDot={{ r: 6, fill: '#F5F5F5' }} />
                            </LineChart>
                        </ResponsiveContainer>
                    </div>
                </motion.div>

                <motion.div initial={{ opacity: 0, y: 30 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 0.5 }} className="mt-12 p-8 border"
                    style={{
                        background: 'linear-gradient(135deg, #0D0D10 0%, #000000 100%)',
                        borderColor: '#1F1F26', borderRadius: '2px',
                    }}>
                    <h3 className="font-serif text-xl font-light text-cx-cream mb-4">
                        Управление{' '}
                        <span className="italic"
                            style={{
                                background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                            }}>ботом</span>
                    </h3>
                    <p className="text-sm font-light leading-relaxed" style={{ color: '#A8A8A8' }}>
                        Сейчас весь функционал бота управляется прямо в Телеграм. Открой главный бот{' '}
                        <a href="https://t.me/cl1ent1x_bot" target="_blank" rel="noopener"
                            className="font-medium hover:underline" style={{ color: PLATINUM }}>@cl1ent1x_bot</a>
                        {' '}— там услуги, расписание, портфолио, статистика.
                    </p>
                </motion.div>
            </main>
        </div>
    );
}

interface PeriodCardProps {
    title: string;
    stats?: { total: number; completed: number; (cancelledByClient ?? 0) + (cancelledByMaster ?? 0): number; noShow: number; revenueRub: number };
    delay?: number;
    highlight?: boolean;
}

function PeriodCard({ title, stats, delay = 0, highlight }: PeriodCardProps) {
    return (
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, delay }}
            whileHover={{ y: -4, boxShadow: '0 0 30px rgba(232,232,232,0.15)' }}
            className="relative p-6 border transition-all duration-300"
            style={{
                background: highlight
                    ? 'linear-gradient(135deg, #1F1F26 0%, #0D0D10 100%)'
                    : 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                borderColor: highlight ? PLATINUM : '#1F1F26',
                borderRadius: '2px',
            }}>
            <p className="text-xs tracking-widest uppercase mb-4" style={{ color: '#6B6B6B' }}>{title}</p>
            <div className="flex items-baseline gap-2 mb-4">
                <span className="font-serif text-5xl font-light"
                    style={{
                        background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                        lineHeight: '1.2', paddingBottom: '0.1em',
                    }}>
                    <AnimatedCounter target={stats?.count ?? 0} duration={1.2} />
                </span>
                <span className="text-sm font-light" style={{ color: '#A8A8A8' }}>записей</span>
            </div>
            <div className="space-y-2 text-sm">
                <Row icon={CheckCircle} label="Выполнено" value={stats?.completed ?? 0} color="#4ADE80" />
                <Row icon={XCircle} label="Отменено" value={stats?.cancelled ?? 0} color="#F87171" />
                <Row icon={AlertCircle} label="Не пришли" value={stats?.noShow ?? 0} color="#FBBF24" />
            </div>
            <div className="mt-4 pt-4 border-t" style={{ borderColor: '#1F1F26' }}>
                <div className="flex items-baseline justify-between">
                    <span className="text-xs tracking-widest uppercase" style={{ color: '#6B6B6B' }}>Доход</span>
                    <span className="font-serif text-2xl font-light italic"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                            WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                            lineHeight: '1.2', paddingBottom: '0.1em',
                        }}>
                        <AnimatedCounter target={stats?.revenue ?? 0} duration={1.5}
                            format={(v) => `${v.toLocaleString('ru-RU')} ₽`} />
                    </span>
                </div>
            </div>
        </motion.div>
    );
}

function Row({ icon: Icon, label, value, color }: any) {
    return (
        <div className="flex items-center justify-between">
            <span className="flex items-center gap-2" style={{ color: '#A8A8A8' }}>
                <Icon className="w-3.5 h-3.5" style={{ color }} />{label}
            </span>
            <span className="font-medium" style={{ color: '#D4D4D4' }}>
                <AnimatedCounter target={value} duration={1} />
            </span>
        </div>
    );
}

function generateMockChartData() {
    return Array.from({ length: 30 }, (_, i) => ({
        day: `${i + 1}`,
        count: Math.floor(Math.random() * 8) + 1,
    }));
}