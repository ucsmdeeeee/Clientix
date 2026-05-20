import { Hero } from './sections/Hero';
import { Features } from './sections/Features';
import { HowItWorks } from './sections/HowItWorks';
import { Pricing } from './sections/Pricing';
import { Faq } from './sections/Faq';
import { Footer } from './sections/Footer';

function App() {
    return (
        <div className="min-h-screen bg-cx-black text-cx-silver">
            <Hero />
            <Features />
            <HowItWorks />
            <Pricing />
            <Faq />
            <Footer />
        </div>
    );
}

export default App;