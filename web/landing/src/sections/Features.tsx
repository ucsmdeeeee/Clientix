import { motion } from 'framer-motion';
import { Calendar, Bell, BarChart3, Briefcase, CreditCard, Globe } from 'lucide-react';
import { TiltCard } from '../components/TiltCard';
import { Particles } from '../components/Particles';

const PLATINUM = '#E8E8E8';

const features = [
    {
        icon: Calendar,
        title: 'Запись в три клика',
        desc: 'Клиент выбирает услугу, дату и время. Никаких форм, регистраций и сложностей.',
    },
    {
        icon: Bell,
        title: 'Авто-напоминания',
        desc: 'За 24 часа и за час до записи. Клиент не забудет, ты не теряешь деньги.',
    },
    {
        icon: BarChart3,
        title: 'Статистика и доход',
        desc: 'Все записи и доход за день, неделю, месяц. Доходимость клиентов в одном окне.',
    },
    {
        icon: Briefcase,
        title: 'Свой бот, своё лицо',
        desc: 'Клиенты записываются через твой бот с твоим именем и портфолио. Бренд — твой.',
    },
    {
        icon: CreditCard,
        title: 'Без комиссий',
        desc: 'Подписка от 300 ₽ в месяц. Никаких процентов с записей. Все деньги твои.',
    },
    {
        icon: Globe,
        title: 'Работает по всей России',
        desc: '11 часовых поясов, перенос записей, исключения для отпусков и праздников.',
    },
];

export function Features() {
    return (
        <section
            className="relative py-32 md:py-40 overflow-hidden"
            style={{
                background: 'linear-gradient(180deg, #000000 0%, #0D0D10 50%, #000000 100%)',
            }}
        >
            <div
                className="absolute inset-0 opacity-[0.03]"
                style={{
                    backgroundImage: `radial-gradient(circle, ${PLATINUM} 1px, transparent 1px)`,
                    backgroundSize: '60px 60px',
                }}
            />

            <Particles count={10} />

            <div className="relative z-10 max-w-6xl mx-auto px-6">
                <motion.div
                    initial={{ opacity: 0, y: 40 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: '-100px' }}
                    transition={{ duration: 0.8 }}
                    className="text-center mb-20"
                >
                    <span
                        className="text-xs tracking-[0.3em] uppercase font-medium"
                        style={{ color: PLATINUM }}
                    >
                        Возможности
                    </span>
                    <h2 className="font-serif text-4xl md:text-6xl font-light text-cx-cream mt-4 leading-tight">
                        Всё, что нужно
                        <br />
                        <span className="italic" style={{ color: PLATINUM }}>
                            для работы с клиентами
                        </span>
                    </h2>
                </motion.div>

                <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {features.map((feature, index) => {
                        const Icon = feature.icon;
                        return (
                            <motion.div
                                key={feature.title}
                                initial={{ opacity: 0, y: 40 }}
                                whileInView={{ opacity: 1, y: 0 }}
                                viewport={{ once: true, margin: '-50px' }}
                                transition={{ duration: 0.6, delay: index * 0.08 }}
                            >
                                <TiltCard className="h-full">
                                    <div
                                        className="group relative h-full p-10 transition-all duration-500 border"
                                        style={{
                                            background: 'linear-gradient(135deg, #16161A 0%, #0D0D10 100%)',
                                            borderColor: '#1F1F26',
                                            borderRadius: '4px',
                                        }}
                                    >
                                        <motion.div
                                            className="mb-6 w-12 h-12 flex items-center justify-center"
                                            whileHover={{ scale: 1.1, rotate: 5 }}
                                            transition={{ type: 'spring', stiffness: 300 }}
                                        >
                                            <Icon
                                                className="w-8 h-8"
                                                style={{ color: PLATINUM, strokeWidth: 1.2 }}
                                            />
                                        </motion.div>

                                        <h3 className="font-serif text-2xl font-light text-cx-cream mb-3">
                                            {feature.title}
                                        </h3>
                                        <p className="text-sm leading-relaxed font-light" style={{ color: '#A8A8A8' }}>
                                            {feature.desc}
                                        </p>

                                        <div
                                            className="absolute bottom-0 left-0 w-0 h-px group-hover:w-full transition-all duration-700"
                                            style={{ background: PLATINUM }}
                                        />

                                        <div
                                            className="absolute top-0 right-0 w-1 h-1 rounded-full opacity-0 group-hover:opacity-100 transition-opacity duration-500"
                                            style={{
                                                background: PLATINUM,
                                                boxShadow: `0 0 10px ${PLATINUM}`,
                                            }}
                                        />
                                    </div>
                                </TiltCard>
                            </motion.div>
                        );
                    })}
                </div>
            </div>
        </section>
    );
}