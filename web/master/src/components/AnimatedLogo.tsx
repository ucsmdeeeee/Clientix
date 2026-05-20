import { motion } from 'framer-motion';
import logo from '../assets/logo.jpg';

const PLATINUM = '#E8E8E8';
const PLATINUM_LIGHT = '#F5F5F5';

interface AnimatedLogoProps { size?: 'sm' | 'md' | 'lg'; }

export function AnimatedLogo({ size = 'md' }: AnimatedLogoProps) {
    const dimensions = {
        sm: 'w-20 h-20',
        md: 'w-32 h-32 md:w-40 md:h-40',
        lg: 'w-48 h-48 md:w-60 md:h-60',
    };
    return (
        <div className={`relative ${dimensions[size]}`}>
            <motion.div
                className="absolute inset-[-30%] rounded-full will-change-transform"
                style={{
                    background: 'radial-gradient(circle, rgba(232,232,232,0.35) 0%, rgba(232,232,232,0.1) 30%, transparent 70%)',
                    transform: 'translateZ(0)',
                }}
                animate={{ scale: [1, 1.15, 1], opacity: [0.6, 1, 0.6] }}
                transition={{ duration: 5, repeat: Infinity, ease: 'easeInOut' }} />
            <motion.div
                className="absolute inset-[-12%] rounded-full will-change-transform"
                style={{ border: '1px solid rgba(232,232,232,0.15)', transform: 'translateZ(0)' }}
                animate={{ rotate: 360 }}
                transition={{ duration: 60, repeat: Infinity, ease: 'linear' }}>
                <div className="absolute top-0 left-1/2 -translate-x-1/2 -translate-y-1/2 w-2 h-2 rounded-full"
                    style={{
                        background: PLATINUM_LIGHT,
                        boxShadow: `0 0 10px ${PLATINUM_LIGHT}, 0 0 20px ${PLATINUM}`,
                    }} />
            </motion.div>
            <motion.div
                className="absolute inset-[-4%] rounded-full will-change-transform"
                style={{ border: '1px solid rgba(232,232,232,0.08)', transform: 'translateZ(0)' }}
                animate={{ rotate: -360 }}
                transition={{ duration: 40, repeat: Infinity, ease: 'linear' }} />
            <motion.div
                className="relative w-full h-full will-change-transform"
                style={{ transform: 'translateZ(0)' }}
                animate={{ y: [0, -6, 0] }}
                transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}>
                <img src={logo} alt="ClientiX" className="w-full h-full rounded-full"
                    style={{ boxShadow: `0 0 40px rgba(232,232,232,0.15), 0 0 80px rgba(232,232,232,0.08)` }} />
            </motion.div>
        </div>
    );
}