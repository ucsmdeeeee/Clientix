import { motion } from 'framer-motion';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { TelegramLoginButton } from '../components/TelegramLoginButton';
import { Particles } from '../components/Particles';
import { AnimatedLogo } from '../components/AnimatedLogo';
import { authApi, type TelegramLoginData } from '../lib/api';

const PLATINUM = '#E8E8E8';
const BOT_NAME = 'cl1ent1x_bot';

export function LoginPage() {
    const navigate = useNavigate();
    const [, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);

    const handleTelegramAuth = async (data: TelegramLoginData) => {
        setLoading(true);
        setError(null);
        try {
            const response = await authApi.loginWithTelegram(data);
            localStorage.setItem('jwt', response.data.token);
            localStorage.setItem('user', JSON.stringify(response.data));
            if (response.data.isAdmin) navigate('/admin', { replace: true });
            else navigate('/dashboard', { replace: true });
        } catch (err: any) {
            setError(
                err.response?.data?.error === 'invalid_signature'
                    ? 'Подпись Телеграм не прошла проверку. Попробуй ещё раз.'
                    : 'Ошибка входа. Попробуй позже.'
            );
            setLoading(false);
        }
    };

    return (
        <div className="relative min-h-screen flex items-center justify-center px-6 overflow-hidden"
            style={{ background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)' }}>
            <motion.div className="absolute top-1/4 -left-32 w-96 h-96 rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.10) 0%, rgba(232,232,232,0) 70%)',
                    transform: 'translateZ(0)',
                }}
                animate={{ scale: [1, 1.2, 1], opacity: [0.3, 0.6, 0.3] }}
                transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut' }} />
            <motion.div className="absolute bottom-1/4 -right-32 w-[500px] h-[500px] rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.08) 0%, rgba(232,232,232,0) 70%)',
                    transform: 'translateZ(0)',
                }}
                animate={{ scale: [1.2, 1, 1.2], opacity: [0.4, 0.7, 0.4] }}
                transition={{ duration: 10, repeat: Infinity, ease: 'easeInOut' }} />

            <Particles count={12} />

            <motion.div initial={{ opacity: 0, y: 30 }} animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.8, ease: [0.22, 1, 0.36, 1] }}
                className="relative z-10 w-full max-w-md">
                <motion.div initial={{ scale: 0.7, opacity: 0 }} animate={{ scale: 1, opacity: 1 }}
                    transition={{ duration: 1, ease: [0.22, 1, 0.36, 1] }}
                    className="flex justify-center mb-10">
                    <AnimatedLogo size="md" />
                </motion.div>

                <div className="text-center mb-12">
                    <motion.h1 initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
                        transition={{ duration: 0.6, delay: 0.3 }}
                        className="font-serif text-5xl md:text-6xl font-light mb-2"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                            WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                        }}>ClientiX</motion.h1>
                    <motion.p initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                        transition={{ duration: 0.6, delay: 0.5 }}
                        className="text-xs tracking-[0.3em] uppercase" style={{ color: '#6B6B6B' }}>
                        Кабинет мастера
                    </motion.p>
                </div>

                <motion.h2 initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 0.7 }}
                    className="font-serif text-2xl md:text-3xl font-light text-cx-cream mb-3 text-center">
                    Войти{' '}
                    <span className="italic"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                            WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
                        }}>через Телеграм</span>
                </motion.h2>

                <motion.p initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                    transition={{ duration: 0.6, delay: 0.9 }}
                    className="text-sm font-light text-center mb-8" style={{ color: '#A8A8A8' }}>
                    Используй тот же аккаунт, через который подключал бота.
                </motion.p>

                <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.6, delay: 1.1 }} className="relative p-10 border"
                    style={{
                        background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                        borderColor: '#1F1F26', borderRadius: '2px',
                    }}>
                    <div className="flex flex-col items-center gap-6">
                        {loading ? (
                            <div className="py-8 text-center">
                                <div className="inline-block w-8 h-8 border-2 rounded-full animate-spin"
                                    style={{ borderColor: '#1F1F26', borderTopColor: PLATINUM }} />
                                <p className="mt-4 text-sm" style={{ color: '#A8A8A8' }}>Авторизуем...</p>
                            </div>
                        ) : (
                            <>
                                <TelegramLoginButton
                                    botName={BOT_NAME}
                                    onAuth={handleTelegramAuth}
                                    buttonSize="large"
                                    cornerRadius={4}
                                />
                                <div
                                    className="w-full flex items-center gap-3"
                                    style={{ color: '#6B6B6B' }}
                                >
                                    <div className="flex-1 h-px" style={{ background: '#1F1F26' }} />
                                    <span className="text-[10px] tracking-widest uppercase">или</span>
                                    <div className="flex-1 h-px" style={{ background: '#1F1F26' }} />
                                </div>
                                <motion.a
                                    href="https://t.me/cl1ent1x_bot?start=login"
                                    whileHover={{ scale: 1.02, boxShadow: '0 0 30px rgba(232,232,232,0.3)' }}
                                    whileTap={{ scale: 0.98 }}
                                    className="group w-full flex items-center justify-center gap-3 px-8 py-3 font-medium tracking-wide"
                                    style={{
                                        background: `linear-gradient(135deg, ${PLATINUM} 0%, #F5F5F5 100%)`,
                                        color: '#000000',
                                        borderRadius: '2px',
                                    }}
                                >
                                    Открыть в Telegram →
                                </motion.a>
                            </>
                        )}
                    </div>
                    <div className="mt-8 pt-6 border-t text-center text-xs tracking-wider uppercase"
                        style={{ borderColor: '#1F1F26', color: '#6B6B6B' }}>
                        Безопасный вход через Telegram
                    </div>
                </motion.div>

                <motion.p initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                    transition={{ duration: 0.6, delay: 1.3 }}
                    className="mt-8 text-center text-sm font-light" style={{ color: '#6B6B6B' }}>
                    Нет бота?{' '}
                    <a href="https://t.me/cl1ent1x_bot" target="_blank" rel="noopener"
                        className="font-medium hover:underline" style={{ color: PLATINUM }}>
                        Подключиться →
                    </a>
                </motion.p>
            </motion.div>
        </div>
    );
}