import { motion } from 'framer-motion';
import logo from '../assets/logo.jpg';

const PLATINUM = '#E8E8E8';

export function Footer() {
    return (
        <footer
            className="relative py-20 overflow-hidden border-t"
            style={{
                background: 'linear-gradient(180deg, #000000 0%, #0D0D10 100%)',
                borderColor: '#1F1F26',
            }}
        >
            <div className="relative z-10 max-w-6xl mx-auto px-6">
                <motion.div
                    initial={{ opacity: 0, y: 40 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-50px' }}
                    transition={{ duration: 0.8 }}
                    className="text-center"
                >
                    <motion.div
                        whileHover={{ scale: 1.05 }}
                        transition={{ type: 'spring', stiffness: 300 }}
                        className="flex justify-center mb-8"
                    >
                        <img
                            src={logo}
                            alt="ClientiX"
                            className="w-20 h-20 rounded-full"
                            style={{ boxShadow: '0 0 30px rgba(232,232,232,0.15)' }}
                        />
                    </motion.div>

                    <h3 className="font-serif text-3xl md:text-4xl font-light text-cx-cream mb-3">
                        ClientiX
                    </h3>
                    <p className="text-sm font-light mb-12 max-w-md mx-auto" style={{ color: '#A8A8A8' }}>
                        Бот в Telegram для бьюти, тату и барбер мастеров.
                    </p>

                    <div className="flex flex-wrap items-center justify-center gap-8 mb-12 text-sm">
                        {[
                            { label: 'Главный бот', href: 'https://t.me/cl1ent1x_bot' },
                            { label: 'Поддержка', href: 'https://t.me/ucsmdeeeee' },
                            { label: 'Кабинет мастера', href: '/app' },
                        ].map((link) => (
                            <motion.a
                                key={link.label}
                                href={link.href}
                                target={link.href.startsWith('http') ? '_blank' : undefined}
                                rel="noopener"
                                whileHover={{ y: -2 }}
                                className="font-light transition-colors relative group"
                                style={{ color: '#D4D4D4' }}
                            >
                                <span className="group-hover:text-cx-cream transition-colors">{link.label}</span>
                                <motion.span
                                    className="absolute -bottom-1 left-0 w-full h-px origin-left"
                                    style={{ background: PLATINUM, scaleX: 0 }}
                                    whileHover={{ scaleX: 1 }}
                                    transition={{ duration: 0.3 }}
                                />
                            </motion.a>
                        ))}
                    </div>

                    <div
                        className="h-px w-24 mx-auto mb-8"
                        style={{ background: PLATINUM, opacity: 0.3 }}
                    />

                    <p className="text-xs tracking-wider uppercase" style={{ color: '#6B6B6B' }}>
                        © 2026 ClientiX. Все права защищены.
                    </p>
                </motion.div>
            </div>
        </footer>
    );
}