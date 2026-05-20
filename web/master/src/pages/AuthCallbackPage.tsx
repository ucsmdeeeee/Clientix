import { motion } from 'framer-motion';
import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import axios from 'axios';
import { AnimatedLogo } from '../components/AnimatedLogo';

const PLATINUM = '#E8E8E8';
const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api';

export function AuthCallbackPage() {
    const navigate = useNavigate();
    const [params] = useSearchParams();
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const shortToken = params.get('t');
        if (!shortToken) {
            setError('Токен не найден в ссылке');
            return;
        }

        // Обмениваем короткий токен на долгий через WebApi
        axios
            .post(`${API_BASE}/auth/exchange`, { shortToken })
            .then((response) => {
                localStorage.setItem('jwt', response.data.token);
                localStorage.setItem('user', JSON.stringify(response.data));

                if (response.data.isAdmin) {
                    navigate('/admin', { replace: true });
                } else {
                    navigate('/dashboard', { replace: true });
                }
            })
            .catch((err) => {
                const errCode = err.response?.data?.error;
                if (errCode === 'invalid_token') {
                    setError('Ссылка истекла или недействительна. Запроси новую в боте.');
                } else if (errCode === 'wrong_token_type') {
                    setError('Неверный тип токена.');
                } else {
                    setError('Ошибка авторизации. Попробуй ещё раз.');
                }
            });
    }, [params, navigate]);

    return (
        <div
            className="relative min-h-screen flex items-center justify-center px-6 overflow-hidden"
            style={{ background: 'radial-gradient(ellipse at top, #0D0D10 0%, #000000 70%)' }}
        >
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                className="text-center"
            >
                <div className="mb-8 flex justify-center">
                    <AnimatedLogo size="sm" />
                </div>

                {error ? (
                    <>
                        <h2 className="font-serif text-2xl font-light text-cx-cream mb-3">
                            Ошибка входа
                        </h2>
                        <p className="text-sm font-light mb-6 max-w-sm mx-auto" style={{ color: '#F87171' }}>
                            {error}
                        </p>
                        <a
                            href="/app/"
                            className="inline-block px-6 py-3 font-medium tracking-wide border transition-colors hover:border-cx-platinum"
                            style={{
                                borderColor: 'rgba(212,212,212,0.2)',
                                color: '#D4D4D4',
                                borderRadius: '2px',
                            }}
                        >
                            На страницу входа
                        </a>
                    </>
                ) : (
                    <>
                        <h2 className="font-serif text-2xl font-light text-cx-cream mb-2">
                            Входим в кабинет...
                        </h2>
                        <p className="text-sm font-light" style={{ color: '#A8A8A8' }}>
                            Подождите секунду
                        </p>
                        <div
                            className="mt-6 inline-block w-6 h-6 border-2 rounded-full animate-spin"
                            style={{
                                borderColor: '#1F1F26',
                                borderTopColor: PLATINUM,
                            }}
                        />
                    </>
                )}
            </motion.div>
        </div>
    );
}