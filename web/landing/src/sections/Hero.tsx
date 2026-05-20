import { motion } from 'framer-motion';
import { ArrowRight } from 'lucide-react';
import logo from '../assets/logo.jpg';
import { Particles } from '../components/Particles';
import { SplitText } from '../components/SplitText';

const PLATINUM = '#E8E8E8';
const PLATINUM_LIGHT = '#F5F5F5';

export function Hero() {
    return (
        <section
            className="relative min-h-screen flex items-center justify-center overflow-hidden py-20 md:py-0"
            style={{
                background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)',
            }}
        >
            <motion.div
                className="absolute top-1/4 -left-32 w-96 h-96 rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.10) 0%, rgba(232,232,232,0) 70%)',
                    transform: 'translateZ(0)',
                }}
                animate={{ scale: [1, 1.2, 1], opacity: [0.3, 0.6, 0.3] }}
                transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut' }}
            />
            <motion.div
                className="absolute bottom-1/4 -right-32 w-[500px] h-[500px] rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.08) 0%, rgba(232,232,232,0) 70%)',
                    transform: 'translateZ(0)',
                }}
                animate={{ scale: [1.2, 1, 1.2], opacity: [0.4, 0.7, 0.4] }}
                transition={{ duration: 10, repeat: Infinity, ease: 'easeInOut' }}
            />

            <div
                className="absolute inset-0 opacity-[0.04]"
                style={{
                    backgroundImage: `radial-gradient(circle, ${PLATINUM} 1px, transparent 1px)`,
                    backgroundSize: '40px 40px',
                }}
            />

            <Particles count={14} />

            <div className="relative z-10 max-w-5xl mx-auto px-6 text-center">
                <motion.div
                    initial={{ opacity: 0, scale: 0.7, y: -20 }}
                    animate={{ opacity: 1, scale: 1, y: 0 }}
                    transition={{ duration: 1.2, ease: [0.22, 1, 0.36, 1] }}
                    className="mb-12 flex justify-center"
                >
                    <div className="relative w-48 h-48 md:w-60 md:h-60">
                        <motion.div
                            className="absolute inset-[-30%] rounded-full will-change-transform"
                            style={{
                                background: 'radial-gradient(circle, rgba(232,232,232,0.35) 0%, rgba(232,232,232,0.1) 30%, transparent 70%)',
                                transform: 'translateZ(0)',
                            }}
                            animate={{ scale: [1, 1.15, 1], opacity: [0.6, 1, 0.6] }}
                            transition={{ duration: 5, repeat: Infinity, ease: 'easeInOut' }}
                        />
                        <motion.div
                            className="absolute inset-[-12%] rounded-full will-change-transform"
                            style={{
                                border: '1px solid rgba(232,232,232,0.15)',
                                transform: 'translateZ(0)',
                            }}
                            animate={{ rotate: 360 }}
                            transition={{ duration: 60, repeat: Infinity, ease: 'linear' }}
                        >
                            <div
                                className="absolute top-0 left-1/2 -translate-x-1/2 -translate-y-1/2 w-2 h-2 rounded-full"
                                style={{
                                    background: PLATINUM_LIGHT,
                                    boxShadow: `0 0 10px ${PLATINUM_LIGHT}, 0 0 20px ${PLATINUM}`,
                                }}
                            />
                        </motion.div>
                        <motion.div
                            className="absolute inset-[-4%] rounded-full will-change-transform"
                            style={{
                                border: '1px solid rgba(232,232,232,0.08)',
                                transform: 'translateZ(0)',
                            }}
                            animate={{ rotate: -360 }}
                            transition={{ duration: 40, repeat: Infinity, ease: 'linear' }}
                        />
                        <motion.div
                            className="relative w-full h-full will-change-transform"
                            style={{ transform: 'translateZ(0)' }}
                            animate={{ y: [0, -8, 0] }}
                            transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
                        >
                            <img
                                src={logo}
                                alt="ClientiX"
                                className="w-full h-full rounded-full"
                                style={{
                                    boxShadow: `0 0 40px rgba(232,232,232,0.15), 0 0 80px rgba(232,232,232,0.08)`,
                                }}
                            />
                            <motion.div
                                className="absolute inset-0 rounded-full overflow-hidden pointer-events-none"
                                style={{ transform: 'translateZ(0)' }}
                            >
                                <motion.div
                                    className="absolute w-[150%] h-[150%] -top-1/4 -left-1/4 will-change-transform"
                                    style={{
                                        background: 'linear-gradient(115deg, transparent 40%, rgba(255,255,255,0.15) 50%, transparent 60%)',
                                        transform: 'translateZ(0)',
                                    }}
                                    animate={{ x: ['-100%', '100%'] }}
                                    transition={{ duration: 4, repeat: Infinity, ease: 'easeInOut', repeatDelay: 2 }}
                                />
                            </motion.div>
                        </motion.div>
                    </div>
                </motion.div>

                <h1 className="font-serif text-4xl md:text-6xl lg:text-7xl font-light text-cx-cream mb-6 leading-tight tracking-tight">
                    <SplitText
                        text="Твой персональный бот."
                        className="block"
                        delay={0.3}
                    />
                    <SplitText
                        text="Твой бренд. Твои клиенты."
                        className="block italic"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, ${PLATINUM_LIGHT} 50%, ${PLATINUM} 100%)`,
                            WebkitBackgroundClip: 'text',
                            WebkitTextFillColor: 'transparent',
                            backgroundClip: 'text',
                        }}
                        delay={1.0}
                    />
                </h1>

                <motion.p
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.8, delay: 1.8 }}
                    className="text-lg md:text-xl mb-12 max-w-2xl mx-auto leading-relaxed font-light"
                    style={{ color: '#A8A8A8' }}
                >
                    Бот в Telegram для бьюти, тату и барбер мастеров.
                    <br />
                    Записи, напоминания, портфолио и статистика — всё в одном месте.
                </motion.p>

                <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.8, delay: 2.0 }}
                    className="flex flex-col sm:flex-row gap-4 justify-center items-center"
                >
                    <motion.a
                        href="https://t.me/cl1ent1x_bot"
                        target="_blank"
                        rel="noopener"
                        whileHover={{ scale: 1.03, boxShadow: '0 0 40px rgba(232,232,232,0.4)' }}
                        whileTap={{ scale: 0.98 }}
                        className="group relative inline-flex items-center gap-3 px-10 py-4 font-medium tracking-wide"
                        style={{
                            background: `linear-gradient(135deg, ${PLATINUM} 0%, ${PLATINUM_LIGHT} 100%)`,
                            color: '#000000',
                            borderRadius: '2px',
                        }}
                    >
                        <span className="relative z-10">Начать бесплатно</span>
                        <ArrowRight className="relative z-10 w-4 h-4 group-hover:translate-x-1 transition-transform" />
                    </motion.a>

                    <motion.a
                        href="#how"
                        whileHover={{ borderColor: PLATINUM }}
                        className="px-10 py-4 font-medium tracking-wide border transition-all"
                        style={{
                            borderColor: 'rgba(212,212,212,0.2)',
                            color: '#D4D4D4',
                            borderRadius: '2px',
                        }}
                    >
                        Узнать больше
                    </motion.a>
                </motion.div>

                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ duration: 1, delay: 2.3 }}
                    className="mt-16 flex items-center justify-center gap-6 text-xs tracking-widest uppercase"
                    style={{ color: '#6B6B6B' }}
                >
                    <span>7 дней бесплатно</span>
                    <span style={{ color: PLATINUM }}>·</span>
                    <span>Без карты</span>
                    <span style={{ color: PLATINUM }}>·</span>
                    <span>5 минут на подключение</span>
                </motion.div>
            </div>

            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                transition={{ delay: 2.5, duration: 1 }}
                className="hidden md:flex absolute bottom-8 left-1/2 -translate-x-1/2 flex-col items-center gap-2"
            >
                <span
                    className="text-[10px] tracking-[0.3em] uppercase"
                    style={{ color: '#6B6B6B' }}
                >
                    Scroll
                </span>
                <motion.div
                    animate={{ y: [0, 8, 0] }}
                    transition={{ duration: 2, repeat: Infinity }}
                    className="w-px h-12"
                    style={{ background: `linear-gradient(180deg, ${PLATINUM} 0%, transparent 100%)` }}
                />
            </motion.div>
        </section>
    );
}