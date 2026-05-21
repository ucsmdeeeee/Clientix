import { motion } from 'framer-motion';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    Users, Bot, CreditCard, Calendar, CheckCircle, DollarSign,
    LogOut, Search, Eye,
} from 'lucide-react';
import {
    adminApi,
    type AdminDashboard,
    type AdminMaster,
    logout,
    getUser,
} from '../lib/api';
import { Particles } from '../components/Particles';
import { AnimatedCounter } from '../components/AnimatedCounter';
import logo from '../assets/logo.jpg';

const PLATINUM = '#E8E8E8';

export function AdminPage() {
    const navigate = useNavigate();
    const [dashboard, setDashboard] = useState<AdminDashboard | null>(null);
    const [masters, setMasters] = useState<AdminMaster[]>([]);
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState('');
    const user = getUser();

    useEffect(() => {
        Promise.all([adminApi.getDashboard(), adminApi.getMasters()])
            .then(([dRes, mRes]) => {
                setDashboard(dRes.data);
                setMasters(mRes.data);
            })
            .catch((err) => {
                console.error('Admin load error', err);
                if (err.response?.status === 403) {
                    navigate('/dashboard', { replace: true });
                }
            })
            .finally(() => setLoading(false));
    }, [navigate]);

    if (loading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-cx-black">
                <div className="w-10 h-10 border-2 rounded-full animate-spin"
                    style={{ borderColor: '#1F1F26', borderTopColor: PLATINUM }} />
            </div>
        );
    }

    const filteredMasters = masters.filter((m) => {
        const q = search.toLowerCase();
        if (!q) return true;
        return (
            m.firstName?.toLowerCase().includes(q) ||
            m.username?.toLowerCase().includes(q) ||
            m.botUsername?.toLowerCase().includes(q) ||
            m.city?.toLowerCase().includes(q) ||
            m.niche?.toLowerCase().includes(q) ||
            String(m.telegramId).includes(q)
        );
    });

    return (
        <div className="relative min-h-screen overflow-hidden"
            style={{ background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)' }}>
            <Particles count={8} />

            <header className="sticky top-0 z-50 border-b backdrop-blur-md"
                style={{ borderColor: '#1F1F26', background: 'rgba(0,0,0,0.85)' }}>
                <div className="max-w-7xl mx-auto px-6 py-4 flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <img src={logo} alt="ClientiX" className="w-10 h-10 rounded-full"
                            style={{ boxShadow: '0 0 15px rgba(232,232,232,0.2)' }} />
                        <div>
                            <h1 className="font-serif text-xl font-light"
                                style={{
                                    background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                    WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                                }}>ClientiX</h1>
                            <p className="text-[10px] tracking-widest uppercase" style={{ color: '#6B6B6B' }}>Админ-панель</p>
                        </div>
                    </div>
                    <div className="flex items-center gap-4">
                        <div className="text-right hidden md:block">
                            <p className="text-sm font-medium text-cx-cream">{user?.firstName || 'Админ'}</p>
                            <p className="text-xs" style={{ color: PLATINUM }}>Администратор</p>
                        </div>
                        <motion.button whileHover={{ scale: 1.05, borderColor: PLATINUM }} whileTap={{ scale: 0.95 }}
                            onClick={logout} className="p-2 border transition-colors"
                            style={{ borderColor: '#1F1F26', borderRadius: '2px' }} title="Выйти">
                            <LogOut className="w-4 h-4" style={{ color: '#A8A8A8' }} />
                        </motion.button>
                    </div>
                </div>
            </header>

            <main className="relative z-10 max-w-7xl mx-auto px-6 py-12">
                <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6 }} className="mb-12">
                    <h2 className="font-serif text-4xl md:text-5xl font-light text-cx-cream mb-2">
                        Управление{' '}
                        <span className="italic"
                            style={{
                                background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                            }}>платформой</span>
                    </h2>
                    <p className="text-base font-light" style={{ color: '#A8A8A8' }}>
                        Общая статистика и список мастеров
                    </p>
                </motion.div>

                {/* Stats cards */}
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-12">
                    <StatCard icon={Users} title="Всего мастеров"
                        value={dashboard?.totalMasters ?? 0} color="#E8E8E8" delay={0.05} />
                    <StatCard icon={Bot} title="Активных ботов"
                        value={dashboard?.activeBots ?? 0} color="#4ADE80" delay={0.1} />
                    <StatCard icon={CreditCard} title="Платных подписок"
                        value={dashboard?.paying ?? 0} color="#FBBF24" delay={0.15} />
                    <StatCard icon={CreditCard} title="На пробном"
                        value={dashboard?.trial ?? 0} color="#A8A8A8" delay={0.2} />
                    <StatCard icon={Calendar} title="Записей 30д"
                        value={dashboard?.totalBookings30d ?? 0} color="#E8E8E8" delay={0.25} />
                    <StatCard icon={CheckCircle} title="Выполнено 30д"
                        value={dashboard?.completedBookings30d ?? 0} color="#4ADE80" delay={0.3} />
                    <StatCard icon={DollarSign} title="Оборот 30д ₽"
                        value={dashboard?.revenue30d ?? 0} color="#FBBF24" delay={0.35} highlight />
                </div>

                {/* Masters table */}
                <motion.div initial={{ opacity: 0, y: 30 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 0.4 }} className="border"
                    style={{
                        background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                        borderColor: '#1F1F26', borderRadius: '2px',
                    }}>
                    <div className="flex items-center justify-between p-6 border-b" style={{ borderColor: '#1F1F26' }}>
                        <h3 className="font-serif text-2xl font-light text-cx-cream">
                            Мастера{' '}
                            <span className="text-sm font-sans" style={{ color: '#6B6B6B' }}>
                                ({filteredMasters.length})
                            </span>
                        </h3>
                        <div className="relative">
                            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4"
                                style={{ color: '#6B6B6B' }} />
                            <input type="text" value={search} onChange={(e) => setSearch(e.target.value)}
                                placeholder="Поиск..."
                                className="pl-9 pr-4 py-2 bg-transparent border text-sm outline-none focus:border-cx-platinum transition-colors"
                                style={{
                                    borderColor: '#1F1F26',
                                    color: '#D4D4D4',
                                    borderRadius: '2px',
                                    minWidth: '200px',
                                }} />
                        </div>
                    </div>

                    {filteredMasters.length === 0 ? (
                        <div className="p-12 text-center" style={{ color: '#6B6B6B' }}>
                            Мастеров не найдено
                        </div>
                    ) : (
                        <div className="overflow-x-auto">
                            <table className="w-full text-sm">
                                <thead>
                                    <tr style={{ borderBottom: '1px solid #1F1F26' }}>
                                        <Th>Имя</Th>
                                        <Th>TG ID</Th>
                                        <Th>Username</Th>
                                        <Th>Бот</Th>
                                        <Th>Город</Th>
                                        <Th>Ниша</Th>
                                        <Th>Подписка</Th>
                                        <Th>Активен</Th>
                                        <Th>Зарегистрирован</Th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {filteredMasters.map((m, idx) => (
                                        <motion.tr key={m.id}
                                            initial={{ opacity: 0, x: -10 }}
                                            animate={{ opacity: 1, x: 0 }}
                                            transition={{ duration: 0.3, delay: idx * 0.02 }}
                                            style={{ borderBottom: '1px solid #1F1F26' }}
                                            className="hover:bg-cx-iron transition-colors">
                                            <Td><span className="font-medium" style={{ color: '#E8E8E8' }}>
                                                {m.firstName || '—'}
                                            </span></Td>
                                            <Td><span style={{ color: '#A8A8A8' }}>{m.telegramId}</span></Td>
                                            <Td>{m.username ? (
                                                <a href={`https://t.me/${m.username}`} target="_blank" rel="noopener"
                                                    className="hover:underline" style={{ color: PLATINUM }}>
                                                    @{m.username}
                                                </a>
                                            ) : <span style={{ color: '#6B6B6B' }}>—</span>}</Td>
                                            <Td>{m.botUsername ? (
                                                <a href={`https://t.me/${m.botUsername}`} target="_blank" rel="noopener"
                                                    className="hover:underline" style={{ color: PLATINUM }}>
                                                    @{m.botUsername}
                                                </a>
                                            ) : <span style={{ color: '#6B6B6B' }}>—</span>}</Td>
                                            <Td><span style={{ color: '#A8A8A8' }}>{m.city || '—'}</span></Td>
                                            <Td><span style={{ color: '#A8A8A8' }}>{m.niche || '—'}</span></Td>
                                            <Td>
                                                <SubscriptionBadge status={m.subscriptionStatus} />
                                            </Td>
                                            <Td>
                                                {m.isActive ? (
                                                    <span style={{ color: '#4ADE80' }}>● Да</span>
                                                ) : (
                                                    <span style={{ color: '#6B6B6B' }}>○ Нет</span>
                                                )}
                                            </Td>
                                            <Td><span style={{ color: '#6B6B6B' }}>
                                                {new Date(m.createdAt).toLocaleDateString('ru-RU')}
                                            </span></Td>
                                        </motion.tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </motion.div>

                <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                    transition={{ duration: 0.6, delay: 0.6 }}
                    className="mt-8 text-center text-xs tracking-wider uppercase"
                    style={{ color: '#6B6B6B' }}>
                    ClientiX Admin v1.0
                </motion.div>
            </main>
        </div>
    );
}

function StatCard({ icon: Icon, title, value, color, delay, highlight }: any) {
    return (
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, delay }}
            whileHover={{ y: -3, boxShadow: '0 0 25px rgba(232,232,232,0.12)' }}
            className="p-5 border transition-all duration-300"
            style={{
                background: highlight
                    ? 'linear-gradient(135deg, #1F1F26 0%, #0D0D10 100%)'
                    : 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                borderColor: highlight ? PLATINUM : '#1F1F26',
                borderRadius: '2px',
            }}>
            <div className="flex items-center justify-between mb-3">
                <p className="text-[10px] tracking-widest uppercase" style={{ color: '#6B6B6B' }}>{title}</p>
                <Icon className="w-4 h-4" style={{ color }} />
            </div>
            <p className="font-serif text-3xl font-light"
                style={{
                    background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                    WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                    lineHeight: '1.2', paddingBottom: '0.1em',
                }}>
                <AnimatedCounter target={value} duration={1.2}
                    format={highlight ? (v) => v.toLocaleString('ru-RU') : undefined} />
            </p>
        </motion.div>
    );
}

function Th({ children }: any) {
    return (
        <th className="px-4 py-3 text-left text-[10px] tracking-widest uppercase font-medium"
            style={{ color: '#6B6B6B' }}>
            {children}
        </th>
    );
}

function Td({ children }: any) {
    return <td className="px-4 py-3">{children}</td>;
}

function SubscriptionBadge({ status }: { status: string }) {
    const config: any = {
        active: { label: 'Активна', color: '#4ADE80' },
        trial: { label: 'Пробная', color: '#FBBF24' },
        expired: { label: 'Истекла', color: '#F87171' },
        cancelled: { label: 'Отменена', color: '#6B6B6B' },
        none: { label: '—', color: '#6B6B6B' },
    };
    const c = config[status] || config.none;
    return <span style={{ color: c.color }}>{c.label}</span>;
}